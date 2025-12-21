import { createSignal } from 'solid-js';
import type { Store } from '../store';

export function IdenticalEntry(advice: Store['identicalAdvices'][number]) {
  const [ hoverItem, setHoverItem ] = createSignal<typeof advice['items'][number]>();
  const copy = (name: string) => navigator.clipboard.writeText(name);
  return (
    <div class="entry">
      <div class="entry--title">
        <div class="entry--name">{advice.items[0].name}, …</div>
        <div>
          {advice.dyed
            ? advice.dyeDiff
              ? <span class="badge badge--orange">不同染色</span>
              : <span class="badge badge--green">相同染色</span>
            : <span class="badge badge--green">无染色</span>}
        </div>
      </div>
      <div class="entry--items" onMouseLeave={[setHoverItem, undefined]}>
        {advice.items.map(item => (
          <span
            class="entry--item-icon"
            style={{
              'background-image': `url('./icon/${item.id}${item.hq ? 'hq' : ''}')`,
            }}
            onMouseEnter={[setHoverItem, item]}
            onClick={[copy, item.name]}
          >
            {item.dyes.map(dye => (
              <span
                class="entry--item-dye"
                style={dye.id > 0 ? `background: #${dye.color}` : undefined}
              />
            ))}
          </span>
        ))}
      </div>
      {hoverItem() !== undefined && (
        <div class="entry--item-info">
          <div class="entry--name">{hoverItem()!.name}{hoverItem()!.hq ? 'HQ' : ''}</div>
          <div>
            {hoverItem()!.dyes.map(dye => dye.id != 0 && (
              <span class={`badge badge--${dye.expensive ? 'orange' : 'blue'}`}>{dye.name}</span>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
