---
name: data-to-insight
description: Turn raw numbers into a short, checked finding a manager can act on - frame the question, profile and clean the data, compare against target/previous/peers, find the cause, choose the right chart, and write the conclusion with its evidence. Use when the user asks to analyze, compare, find the reason for, track, report on or "make sense of" data, KPIs, a trend, a variance, or a table - before or alongside Excel, Word or PowerPoint output.
---

# Data to insight

Goal: an answer, not a table. The user should finish reading and know what happened, why, how sure we are,
and what to do. The method is the same for 50 rows or 500,000.

## 1. Frame the question

| Fix this first | Example |
|---|---|
| The decision behind the request | "Do we add a second shift?" not "analyze output" |
| The measure and its exact definition | Defect rate = defects / units inspected, per week |
| The comparison | Against target, against last period, against other lines |
| The scope | Which period, plants, products; what is excluded |

A number means nothing alone. `Yield 94%` becomes information only beside the target (96%), last month
(95%), or the other line (97%). Choose the comparison before calculating.

## 2. Know the data before trusting it

Profile every source once:

| Check | What to look for |
|---|---|
| Size and grain | Row count; what one row represents; the period covered |
| Completeness | Blank cells per column; missing days, weeks or categories |
| Validity | Negative quantities, impossible dates, percentages above 100 |
| Duplicates | The same key twice; subtotal rows mixed into the data |
| Consistency | One unit and one name per thing (`Line 1` / `L1` / `line-1`) |
| Extremes | The largest and smallest values: real events or entry mistakes? |

Then reconcile: the total of the data must match a figure the user already trusts (the monthly report, the
system total). If it does not match, find the reason before any analysis. State what was cleaned or
excluded and how many rows it affected.

## 3. Analyze from big to small

1. **Total**: what is the overall level against the comparison?
2. **Trend**: how did it move over time? Is the last point a real change or normal variation?
3. **Breakdown**: split the gap by one dimension at a time (product, line, shift, supplier, cause).
   Find where the difference is concentrated - usually a few items explain most of it.
4. **Cause**: for the biggest contributors, what changed? Volume, mix, price, rate, a single event?
5. **Size it**: express each contributor in the unit the manager uses (units, hours, money).

Guard against the common mistakes:

| Mistake | Guard |
|---|---|
| Averaging percentages | Recalculate from the totals (sum of defects / sum of units) |
| Comparing unequal periods | Same number of working days, or use per-day rates |
| Small numbers looking dramatic | Show the count beside the percentage (`50%, 2 of 4`) |
| Two things moving together taken as cause | Say "moves with", unless the mechanism is confirmed |
| A total hiding opposite movements | Always look one level below the total |
| Choosing only the data that agrees | Report what contradicts the finding too |

## 4. Choose the display

| To show | Use |
|---|---|
| Change over time | Line chart (bars if few periods) |
| Comparison between items | Horizontal bars, sorted largest first |
| Actual against target | Bars with a target line, or a variance column |
| What explains a gap | Waterfall: start, each contributor, end |
| A few items causing most of a problem | Pareto: sorted bars with cumulative line |
| Parts of a whole | Stacked bar; a pie only for two or three parts |
| Relationship between two measures | Scatter plot |
| Exact values needed | Table, sorted, with the key row highlighted |

One chart, one message. The title states the finding; grey for context, one accent colour for the point;
units and source shown; axis for bars starts at zero.

## 5. Write the finding

Use this order. It is the same in an email, a slide or a report.

```
Answer:   <One sentence: what happened, with the number and the comparison.>
Evidence: <Two or three facts, each with numbers, that prove the answer.>
Cause:    <What drives it, and whether this is confirmed or likely.>
Action:   <What to do, who, by when - or the decision needed.>
Note:     <Data limits, assumptions, what was excluded.>
```

Example:

> **Answer:** Line 3 output fell 8% in October (46,200 vs 50,100 units), all of it in weeks 42-43.
> **Evidence:** Lines 1 and 2 were flat (+0.4%). Line 3 lost 31 hours to unplanned stops, against 6 in September.
> **Cause:** 27 of the 31 hours were one fault on the sealing unit (confirmed in the maintenance log).
> **Action:** Replace the sealing unit heater at the 22 November stop; owner: maintenance.
> **Note:** Week 44 data is incomplete (2 shifts missing) and is excluded.

Rules: round for reading (`4.3 M`, not `4,312,877.42`) but calculate with full precision; give the period
and unit with every number; say plainly when the data cannot answer the question.

## 6. Check before delivering

1. Totals reconcile to the trusted figure.
2. Every number in the text matches the table or chart it came from.
3. Recalculate the headline number a second way.
4. The same measure has the same value everywhere in the output.
5. Would the finding survive the question "compared to what?" and "how do you know?"

Then hand over to the output skill that fits: `excel-automation` for the workbook, `powerpoint-design` for
slides, `word-professional` for a report, `formal-writing` for the message.
