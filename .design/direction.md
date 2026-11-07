# Direction — The verification console

## PRODUCT / USER / PRIMARY GOAL
Break Me If You Can is an engineering proof lab for recruiters, senior engineers and
hiring managers. In five seconds a visitor must read one thing: does this release verify,
and can I check it myself? The primary action is *inspect evidence*, not sign up.
The secondary action is to attack the ledger in the arena and watch the database hold.

## VISUAL DIRECTION
A calibrated verification instrument: near-black warm mineral ground, hairline rules,
oversized editorial serif verdicts against dense monospaced readouts. Closer to a
laboratory report or a flight recorder printout than a SaaS dashboard. No gradients, no
glows, no glass. Everything is either evidence or whitespace.

## HIERARCHY
1. The verdict word (VERIFIED / FAILED / UNKNOWN) and its one-sentence reason.
2. The digest chain: build → attestation → deploy request → runtime.
3. The required-checks ledger, row by row.
4. Test, coverage and audit readouts.
5. Everything else (workflow, image reference, security posture) is reference material.

## LAYOUT
Desktop: a fixed instrument rail (232px) on the left, a wide evidence field, and an
asymmetric hero — 1.35fr of verdict against 1fr of digest chain. The arena is a control
column against a live result field. Mobile: the rail collapses to a horizontal section
strip; heroes stack; the checks table becomes labelled rows.

## TYPOGRAPHY
- Display: Instrument Serif, used only for verdicts and section headlines. Tight leading,
  large optical size, no faux-bold.
- Body/UI: Inter Variable at 15px for prose and controls.
- Data/labels: JetBrains Mono Variable, uppercase micro-labels at 0.12em tracking,
  tabular numbers for every metric.
Type does the hierarchy work; colour only marks state.

## COLOR
- ground #0B0C0E, raised #111316, rule #262A30: warm-neutral near-black, not blue-slate.
- ink #ECE8E1 (warm paper), secondary #B9B3A9, muted #8A857C.
- amber #E3A84E: the instrument's signal colour — verified state, focus, primary action.
- vermillion #E2564A: failed state only. Never decorative.
- stone #8E8B85: unknown state — deliberately colourless, because unknown is not green.
- steel #7FA3B8: secondary data series (latency, throughput), never a status.
Every colour has a job; the palette is mineral and warm, deliberately not the default
success-green developer palette.

## DENSITY / INTERACTION / RESPONSIVE STRATEGY
Dense data, generous margins around it. Controls are small, sharp-edged and mono-labelled.
Hover raises contrast, never size. Focus is a 2px amber outline. Run results stream into a
receipt table with a live aria-live summary. Mobile keeps the numbers, drops the rail and
the decorative rules.

## MOTION
Precise and quiet: 120–220ms, cubic-bezier(0.2, 0.7, 0.2, 1). The digest chain connectors
draw once on load; the verdict word fades up 8px; numbers do not count up. Reduced motion
keeps opacity fades only.

## IMAGERY
none. The subject is evidence; charts and diagrams are drawn in SVG from real data.
Stock photography would undermine the claim.

## SIGNATURE
The digest chain: four nodes on a single line, each with its digest and state, terminating
in the verdict. When the chain is complete it reads as one unbroken circuit; when it is
not, the line visibly stops where the evidence stops.
