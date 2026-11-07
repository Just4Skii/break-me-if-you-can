# Design memory

## Tokens

- Ground: @@#0b0c0e@@, raised @@#111316@@, raised-2 @@#171a1e@@, rule @@#262a30@@, rule-strong @@#3a3f47@@.
- Ink: @@#ece8e1@@ (warm paper), @@#b9b3a9@@, @@#8a857c@@.
- Signal amber @@#e3a84e@@ (verified, focus, primary action); vermillion @@#e2564a@@ (failure only); stone @@#8e8b85@@ (unknown); steel @@#7fa3b8@@ (secondary data series only).
- Type: Instrument Serif (verdicts, 54–108px) / Inter Variable (UI, 15px) / JetBrains Mono Variable (labels 8.5–13px, tabular numbers).
- Spacing: 4 / 8 / 12 / 16 / 24 / 32 / 48 / 64 / 96.
- Radii: 2px controls, 4px rare; panels are square and rule-bounded.
- Motion: 120–220ms, cubic-bezier(0.2, 0.7, 0.2, 1); chain draws once; reduced motion removes movement.

## What the critic caught and how it was fixed

Scores: hierarchy 7, composition 8, typography 8, spacing 8, alignment 7, consistency 7,
distinctiveness 8, usability 7, responsiveness 7, motion 5, color 8, polish 8.

Fixed:
1. **Buried failures.** The ledger listed fourteen verified rows before the five that explain
   why the release is not verified. It now groups "Not verified — 5" first, then verified rows.
2. **Unrealised signature.** The digest chain was a vertical sidebar 700px from the verdict.
   It is now a full-width horizontal axis beneath the verdict, with filled amber nodes for
   observed digests, hollow stone nodes for absent ones, a dashed segment after the last
   observation, and a terminal verdict diamond.
3. **Quiet invariants.** The ledger invariants were 11px muted rows under the metrics. They
   are now their own bordered band under the stat row, at 13px with a "held / violated"
   statement and one line explaining that conflicts and retries are the expected cost.
4. **Measure and type level.** Prose is capped at @@min(68ch, 680px)@@; section titles moved
   to 13px mono; latency mean no longer uppercases its unit.
5. **Interaction affordances.** Metadata chips lost their button-like borders; a completed
   run now states "completed <timestamp>"; sliders have an 18px thumb and a real track; the
   mobile tour contents became a two-column grid instead of a zig-zag wrap.

Not changed, by decision: mobile /arena keeps the controls first because the visitor must
configure the run before there is a result; the instrument rail pattern is deliberate.

## Lesson

The evidence page must lead with what is *not* proven. A page that lists fourteen passes
before five unknowns reads as a success page with caveats, which is exactly the failure mode
this product exists to prevent.
