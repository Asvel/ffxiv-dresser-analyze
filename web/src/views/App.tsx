import { Show } from 'solid-js';
import { Store } from '../store';
import { indexRender } from '../utils';
import { StoreContext } from './useStore';
import { Refresh } from './Refresh';
import { OutfitEntry } from './OutfitEntry';
import { CategoryEntry } from './CategoryEntry';
import { IdenticalEntry } from './IdenticalEntry';
import { About } from './About';

export function App() {
  const store = Store.create();
  (window as any).store = store;
  return (
    <StoreContext.Provider value={store}>
      <div class="app">
        <Show when={store.ready}>
          <Refresh />
          <Show
            when={store.dresserItems.size > 0}
            fallback={<div class="nodata">无数据，请在游戏内打开投影台</div>}
          >
            <h2 tabIndex="0">
              可套装幻影化
              <label>
                <input
                  type="checkbox"
                  checked={store.showSingleItemOutfit}
                  onChange={e => store.showSingleItemOutfit = e.target.checked}
                />
                显示仅拥有其中一件的套装
              </label>
            </h2>
            {indexRender(() => store.outfitAdvices, advice => <OutfitEntry {...advice()} />)}
            <h2 tabIndex="0">可放入收藏柜</h2>
            {indexRender(() => store.cabinetAdvices, advice => <CategoryEntry {...advice()} />)}
            <h2 tabIndex="0">可失物回购<i>（大概范围，以及请注意自己是否满足购买条件）</i></h2>
            {indexRender(() => store.reclaimAdvices, advice => <CategoryEntry {...advice()} />)}
            <h2 tabIndex="0">外观完全相同</h2>
            {indexRender(() => store.identicalAdvices, advice => <IdenticalEntry {...advice()} />)}
          </Show>
          <About />
        </Show>
      </div>
    </StoreContext.Provider>
  );
}
