import { Show, type Accessor, type JSX } from 'solid-js';
import { Store } from '../store';
import { indexRender } from '../utils';
import { StoreContext } from './useStore';
import { Refresh } from './Refresh';
import { OutfitEntry } from './OutfitEntry';
import { CategoryEntry } from './CategoryEntry';
import { IdenticalEntry } from './IdenticalEntry';
import { About } from './About';
import { CollapsibleSection } from './CollapsibleSection';

export function App() {
  const store = Store.create();
  (window as any).store = store;
  return (
    <StoreContext.Provider value={store}>
      <div class="app">
        <Show when={store.ready}>
          <Refresh />
          <Show
            when={store.dresserItems.size > 0 || store.cabinetItems.size > 0}
            fallback={
              <div class="nodata">
                <div>无数据，请在游戏内打开投影台或加载收藏柜</div>
                <small>投影台读取状态：{store.dresserLoaded ? '已加载但没有物品' : '未加载'}</small>
              </div>
            }
          >
            <div class="data-status">
              <span>投影台：{store.dresserItemCount} 件</span>
              <span>
                收藏柜：{store.cabinetLoaded ? `${store.cabinetItemCount} 件` : '未加载'}
              </span>
            </div>
            <CollapsibleSection
              title="可套装幻影化"
              open
            >
              {advicesRender(() => store.outfitAdvices, advice => <OutfitEntry {...advice()} />)}
            </CollapsibleSection>
            <CollapsibleSection title="已完整拥有的套装">
              {advicesRender(() => store.otherOutfitAdvices, advice => <OutfitEntry {...advice()} />)}
            </CollapsibleSection>
            <CollapsibleSection title="已经放入收藏柜">
              {advicesRender(() => store.cabinetOutfitAdvices, advice => <OutfitEntry {...advice()} />)}
            </CollapsibleSection>
            <CollapsibleSection title="可放入收藏柜" open>
              {advicesRender(() => store.cabinetAdvices, advice => <CategoryEntry {...advice()} />)}
            </CollapsibleSection>
            <CollapsibleSection title={<><span>可失物回购</span><i>（大概范围，以及请注意自己是否满足购买条件）</i></>}>
              {advicesRender(() => store.reclaimAdvices, advice => <CategoryEntry {...advice()} />)}
            </CollapsibleSection>
            <CollapsibleSection
              title="外观相同"
              controls={
                <label>
                  <input
                    type="checkbox"
                    checked={store.showSemiIdenticals}
                    onChange={e => store.showSemiIdenticals = e.target.checked}
                  />
                  包括仅主模型相同且可染色的装备
                </label>
              }
            >
              {advicesRender(() => store.showSemiIdenticals ? store.semiIdenticalAdvices : store.identicalAdvices,
                advice => <IdenticalEntry {...advice()} />)}
            </CollapsibleSection>
          </Show>
          <About />
        </Show>
      </div>
    </StoreContext.Provider>
  );
}

function advicesRender<T extends readonly any[], U extends JSX.Element>(
  list: Accessor<T>, mapFn: (item: Accessor<T[number]>, index: number) => U) {
  return list().length > 0 ? indexRender(list, mapFn) : <div class="entry--empty">无</div>;
}
