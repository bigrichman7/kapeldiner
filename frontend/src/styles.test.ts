import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

describe('interface accent colors', () => {
  it('uses the blue palette for primary actions and selected seats', () => {
    const stylesheet = readFileSync(resolve(process.cwd(), 'src/styles.css'), 'utf8');

    expect(stylesheet).toContain('--blue: #2563a6;');
    expect(stylesheet).toContain('.button-primary { background: var(--blue);');
    expect(stylesheet).toContain('.seat-selected { border-color: var(--blue); background: var(--blue);');
    expect(stylesheet).not.toContain('--green');
  });
});
