import type { JSX } from 'solid-js';

interface CollapsibleSectionProps {
  title: JSX.Element,
  children: JSX.Element,
  open?: boolean,
  controls?: JSX.Element,
}

export function CollapsibleSection(props: CollapsibleSectionProps) {
  return (
    <details class="section" open={props.open ?? false}>
      <summary class="section--summary">
        <span class="section--title" role="heading" aria-level="2">{props.title}</span>
        {props.controls !== undefined && (
          <span class="section--controls" onClick={event => event.stopPropagation()}>
            {props.controls}
          </span>
        )}
      </summary>
      <div class="section--body">{props.children}</div>
    </details>
  );
}
