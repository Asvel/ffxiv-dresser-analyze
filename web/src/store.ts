import { batch } from 'solid-js';
import { createMutable } from 'solid-js/store';
import { createLazyMemo } from '@solid-primitives/memo';

interface DresserItem {
  id: number,
  hq: boolean,
  dyes: [number, number],
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
  '',
  '主手', '副手',
  '头部', '身体', '手臂', '', '腿部', '脚部',
  '耳部', '颈部', '腕部', '戒指'
];

async function fetchJson(url: string) {
  const res = await fetch(url);
  return await res.json();
}

let outfits: Outfit[];
let cabinets: Categorized[];
let reclaims: Categorized[];
let identicals: Identical[];
let dyeTypes: Dye[];
let pending = Promise.all([
  fetchJson('./data/outfits').then(v => { outfits = v; }),
  fetchJson('./data/cabinets').then(v => { cabinets = v; }),
  fetchJson('./data/reclaims').then(v => { reclaims = v; }),
  fetchJson('./data/identicals').then(v => { identicals = v }),
  fetchJson('./data/dyes').then(v => {
    dyeTypes = v;
    dyeTypes[101].expensive = true;  // 无瑕白
    dyeTypes[102].expensive = true;  // 煤玉黑
    dyeTypes[103].expensive = true;  // 柔彩粉
    dyeTypes[112].expensive = true;  // 闪耀银
  }),
]);

export class Store {
  ready = false;
  dresserItems: Map<number, DresserItem>;
  retainedOutfitIds: Set<number> = new Set();
  showSingleItemOutfit = false;

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
    const res = await fetch('./data/dresser');
    if (this.dresserItems !== undefined && res.headers.has('X-Not-Modified')) return;

    const data = await res.json() as DresserItem[];
    const dresserItems = new Map<number, DresserItem>();
    for (const item of data) {
      dresserItems.set(item.id, item);
    }
    this.dresserItems = dresserItems;
  }

  get outfitAdvices() {
    const advices = outfits.map(outfit => {
      let count = 0;
      let hqCount = 0;
      let dyeable = false;
      let dyed = false;
      let dyeExpensive = false;
      const items = outfit.items.map(item => {
        const dyes: Dye[] = [];
        const dresserItem = this.dresserItems.get(item.id);
        if (dresserItem !== undefined) {
          count++;
          if (dresserItem.hq) hqCount++;
          for (let i = 0; i < item.dyeCount; i++) {
            var dye = dyeTypes[dresserItem.dyes[i]];
            dyed ||= dye.id > 0;
            dyeExpensive ||= dye.expensive;
            dyes.push(dye);
          }
        }
        dyeable ||= item.dyeCount > 0;
        return {
          ...item,
          hq: dresserItem?.hq,
          dyes,
          acquired: dresserItem !== undefined,
        };
      })
      if (count === 0) return;
      if (count === 1 && !this.showSingleItemOutfit && !this.retainedOutfitIds.has(outfit.id)) return;
      if (count > 1) this.retainedOutfitIds.add(outfit.id);
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
        cabinet: outfit.cabinet,
        mixQuality: hqCount > 0 && hqCount < count,
      }
    }).filter(x => x !== undefined);
    return advices;
  }

  get cabinetAdvices() {
    return this.getCategorizedAdvices(cabinets);
  }
  get reclaimAdvices() {
    return this.getCategorizedAdvices(reclaims);
  }
  getCategorizedAdvices(base: Categorized[]) {
    return base.map(group => {
      const items = group.items.map(item => {
        const dresserItem = this.dresserItems.get(item.id);
        if (dresserItem === undefined) return;
        let dyed = false;
        const dyes: Dye[] = [];
        for (let i = 0; i < item.dyeCount; i++) {
          var dye = dyeTypes[dresserItem.dyes[i]];
          dyed ||= dye.id > 0;
          dyes.push(dye);
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
    const groups = new Map<number, [Identical, DresserItem][]>();
    for (const identical of identicals) {
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
            var dye = dyeTypes[dresserItem.dyes[i]];
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
