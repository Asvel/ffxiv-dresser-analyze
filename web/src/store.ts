import { batch } from 'solid-js';
import { createMutable } from 'solid-js/store';
import { createLazyMemo } from '@solid-primitives/memo';

interface DresserItem {
  id: number,
  hq: boolean,
  dyes: [number, number],
}

interface CabinetItem {
  cabinetId: number,
  id: number,
  name: string,
}

interface Outfit {
  id: number,
  name: string,
  items: {
    id: number,
    name: string,
    dyeCount: number,
  }[],
  cabinet: boolean,
}

interface Categorized {
  category: number,
  items: {
    id: number,
    name: string,
    dyeCount: number,
  }[],
}

interface Identical {
  groupId: number,
  itemId: number
  name: string,
  dyeCount: number,
}

interface Dye {
  id: number,
  name: string,
  color: string,
  expensive: boolean,
}

export enum DyeStatus {
  Unavailable,
  None,
  Some,
  Expensive,
}

const categories = [
  '套装',
  '主手', '副手',
  '头部', '身体', '手臂', '', '腿部', '脚部',
  '耳部', '颈部', '腕部', '戒指'
];

async function fetchJson(url: string) {
  const res = await fetch(url);
  return await res.json();
}

function getDye(id: number): Dye {
  return dyeTypes[id] ?? {
    id,
    name: id > 0 ? `未知染剂（${id}）` : '',
    color: '000000',
    expensive: false,
  };
}

let outfits: Outfit[];
let cabinets: Categorized[];
let cabinetItems: CabinetItem[] = [];
let reclaims: Categorized[];
let identicals: Identical[];
let semiIdenticals: Identical[];
let dyeTypes: Dye[];
let pending = Promise.all([
  fetchJson('./data/outfits').then(v => { outfits = v; }),
  fetchJson('./data/cabinets').then(v => { cabinets = v; }),
  fetchJson('./data/cabinet-items').then(v => { cabinetItems = v; }).catch(() => { cabinetItems = []; }),
  fetchJson('./data/reclaims').then(v => { reclaims = v; }),
  fetchJson('./data/identicals').then(v => { identicals = v }),
  fetchJson('./data/semi-identicals').then(v => { semiIdenticals = v }),
  fetchJson('./data/dyes').then(v => {
    dyeTypes = v;
    // 贵重染剂：无法游戏内稳定获取，并且市场售价≥50000
    dyeTypes[101].expensive = true;  // 无瑕白
    dyeTypes[102].expensive = true;  // 煤玉黑
    dyeTypes[103].expensive = true;  // 柔彩粉
    dyeTypes[112].expensive = true;  // 闪耀银
  }),
]);

export class Store {
  ready = false;
  dresserItems: Map<number, DresserItem>;
  cabinetLoaded = false;
  cabinetItemCount = 0;
  dresserItemCount = 0;
  dresserLoaded = false;
  private dresserOnlyItems = new Map<number, DresserItem>();
  cabinetItems = new Map<number, DresserItem>();
  private loadedCabinetIds = new Set<number>();
  showSemiIdenticals = false;

  static create() {
    const store: Store = createMutable(new Store() as any);
    for (const [ key, desc ] of Object.entries(Object.getOwnPropertyDescriptors(Object.getPrototypeOf(store)))) {
      if (key === 'constructor') continue;
      if (desc.get) {
        const get = createLazyMemo(desc.get.bind(store));
        // const get = createLazyMemo(() => { console.log(desc.get!.name); return desc.get!.apply(store); });
        Object.defineProperty(store, key, { get });
      }
      if (typeof desc.value === 'function') {
        const og = desc.value;
        const value = (...args: any[]) => batch(() => og.apply(store, args));
        Object.defineProperty(store, key, { value });
      }
    }

    Promise.all([pending, store.fetchDresser()]).then(() => store.ready = true);

    return store;
  }

  async fetchDresser() {
    // Cabinet IDs need the static cabinet-to-item table before they can be merged.
    await pending;
    const stamp = Date.now();
    const [dresserRes, cabinetRes] = await Promise.all([
      fetch(`./data/dresser?_=${stamp}`, { cache: 'no-store' }),
      fetch(`./data/cabinet?_=${stamp}`, { cache: 'no-store' }),
    ]);

    if (!dresserRes.headers.has('X-Not-Modified')) {
      const data = await dresserRes.json() as DresserItem[];
      // A transient process read can legitimately return [] while the previous
      // snapshot is still valid. Never replace a non-empty snapshot with that
      // unconfirmed empty response.
      const dataLoaded = dresserRes.headers.get('X-Data-Loaded') !== 'false';
      if (dataLoaded || this.dresserOnlyItems.size === 0 || data.length > 0) {
        const dresserOnlyItems = new Map<number, DresserItem>();
        for (const item of data) {
          dresserOnlyItems.set(item.id, item);
        }
        this.dresserOnlyItems = dresserOnlyItems;
      }
      this.dresserLoaded = dataLoaded;
    }

    if (!cabinetRes.headers.has('X-Not-Modified')) {
      const cabinet = await cabinetRes.json() as { loaded?: boolean, cabinetIds?: number[] };
      this.cabinetLoaded = cabinet.loaded === true;
      this.loadedCabinetIds = this.cabinetLoaded
        ? new Set((cabinet.cabinetIds || []).map(id => Number(id)))
        : new Set();
    }
    const cabinetOwnedItems = new Map<number, DresserItem>();
    if (this.cabinetLoaded) {
      for (const item of cabinetItems) {
        if (!this.loadedCabinetIds.has(Number(item.cabinetId))) continue;
        if (!cabinetOwnedItems.has(item.id)) {
          cabinetOwnedItems.set(item.id, { id: item.id, hq: false, dyes: [0, 0] });
        }
      }
    }
    this.cabinetItems = cabinetOwnedItems;
    this.dresserItemCount = this.dresserOnlyItems.size;
    this.cabinetItemCount = this.cabinetItems.size;
    this.dresserItems = new Map(this.dresserOnlyItems);
  }

  get outfitAdvices() {
    return this.getDresserOutfitAdvices(true).filter(advice =>
      advice.count > 0 && advice.count < advice.items.length);
  }

  get otherOutfitAdvices() {
    return this.getDresserOutfitAdvices(false).filter(advice =>
      advice.count === advice.items.length);
  }

  private getDresserOutfitAdvices(showCabinetBadge: boolean) {
    const source = this.getDresserItemsExcludingCabinet();
    return this.getOutfitAdvices(source, showCabinetBadge);
  }

  get cabinetOutfitAdvices() {
    return this.getOutfitAdvices(this.cabinetItems, false);
  }

  private getOutfitAdvices(source: Map<number, DresserItem>, showCabinetBadge: boolean) {
    const advices = outfits.map(outfit => {
      let count = 0;
      let hqCount = 0;
      let dyeable = false;
      let dyed = false;
      let dyeExpensive = false;
      const outfitSourceItem = source.get(outfit.id);
      const items = outfit.items.map(item => {
        const dyes: Dye[] = [];
        // The dresser can store a glamourized outfit as the outfit row itself
        // instead of its individual component IDs. In that case every component
        // is considered acquired for display and import purposes.
        const sourceItem = source.get(item.id) ?? outfitSourceItem;
        if (sourceItem !== undefined) {
          count++;
          if (sourceItem.hq) hqCount++;
          for (let i = 0; i < item.dyeCount; i++) {
            var dye = getDye(sourceItem.dyes[i]);
            dyed ||= dye.id > 0;
            dyeExpensive ||= dye.expensive;
            dyes.push(dye);
          }
        }
        dyeable ||= item.dyeCount > 0;
        return {
          ...item,
          hq: sourceItem?.hq,
          dyes,
          acquired: sourceItem !== undefined,
        };
      })
      if (count === 0) return;
      if (count < items.length && count === hqCount) {  // 现有全为HQ，缺的也用HQ
        for (const item of items) {
          item.hq = true;
        }
      }
      const dyeStatus = !dyeable
        ? DyeStatus.Unavailable
        : dyeExpensive
          ? DyeStatus.Expensive
          : dyed
            ? DyeStatus.Some
            : DyeStatus.None;
      return {
        id: outfit.id,
        name: outfit.name,
        items,
        count,
        dyeStatus,
        cabinet: showCabinetBadge && outfit.cabinet,
        mixQuality: hqCount > 0 && hqCount < count,
      }
    }).filter(x => x !== undefined);
    return advices;
  }

  get cabinetAdvices() {
    return this.getCategorizedAdvices(cabinets, this.getDresserItemsExcludingCabinet());
  }
  get reclaimAdvices() {
    return this.getCategorizedAdvices(reclaims, this.dresserItems);
  }
  private getDresserItemsExcludingCabinet() {
    const items = new Map<number, DresserItem>();
    for (const [id, item] of this.dresserItems) {
      if (!this.cabinetItems.has(id)) items.set(id, item);
    }
    return items;
  }
  getCategorizedAdvices(base: Categorized[], source = this.dresserItems) {
    return base.map(group => {
      const items = group.items.map(item => {
        const dresserItem = source.get(item.id);
        if (dresserItem === undefined) return;
        let dyed = false;
        const dyes: Dye[] = [];
        if (group.category !== 0/*套装*/) {
          for (let i = 0; i < item.dyeCount; i++) {
            var dye = getDye(dresserItem.dyes[i]);
            dyed ||= dye.id > 0;
            dyes.push(dye);
          }
        }
        return {
          ...item,
          hq: dresserItem.hq,
          dyed,
          dyes,
        };
      }).filter(x => x !== undefined);

      return [{
        category: categories[group.category],
        dyed: false,
        items: items.filter(x => !x.dyed),
      }, {
        category: categories[group.category],
        dyed: true,
        items: items.filter(x => x.dyed),
      }]
    }).flat().filter(group => group.items.length > 0);
  }

  get identicalAdvices() {
    return this.getIdenticalAdvices(identicals);
  }
  get semiIdenticalAdvices() {
    const identicalItems = new Set<number>();
    for (const group of this.identicalAdvices) {
      for (const item of group.items) {
        identicalItems.add(item.id);
      }
    }
    // 只能在前端筛：A,B,C三件装备主模型相同，其中仅A可染色，那么投影台中仅有B,C时不展示，有A时则展示
    return this.getIdenticalAdvices(semiIdenticals)
      .filter(group => group.items.some(item => item.dyes.length > 0 || identicalItems.has(item.id)));
  }
  getIdenticalAdvices(base: Identical[]) {
    const groups = new Map<number, [Identical, DresserItem][]>();
    for (const identical of base) {
        const dresserItem = this.dresserItems.get(identical.itemId);
        if (dresserItem === undefined) continue;
        let group = groups.get(identical.groupId);
        if (group === undefined) {
          group = [];
          groups.set(identical.groupId, group);
        }
        group.push([identical, dresserItem]);
    }
    return Array.from(groups.values())
      .filter(group => group.length > 1)
      .map(group => {
        let dyed = false;
        const dyeSet = new Set<number>();
        var items = group.map(([identical, dresserItem]) => {
          let dyeState = 1;
          const dyes: Dye[] = [];
          for (let i = 0; i < identical.dyeCount; i++) {
            var dye = getDye(dresserItem.dyes[i]);
            dyed ||= dye.id > 0;
            dyeState = (dyeState << 8) + dye.id;
            dyes.push(dye);
          }
          dyeSet.add(dyeState);
          return {
            ...dresserItem,
            name: identical.name,
            dyes,
          };
        });
        return {
          items,
          dyed,
          dyeDiff: dyeSet.size > 1,
        };
      });
  }
}
