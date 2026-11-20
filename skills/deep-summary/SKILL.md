---
name: deep-summary
description: Summarize big topics and long material accurately - long PDFs, reports, manuals, contracts, standards, papers, meeting transcripts, email threads or several documents together - into a layered summary with page references and nothing invented. Use when the user asks to summarize, brief, digest, explain the key points of, extract decisions or actions from, or compare long documents, or asks "what does this say about X".
---

# Deep summary of long material

Goal: the reader gets the truth of the source in a fraction of the time, can choose how deep to read, and can
find any point in the original. A summary that is short but wrong, or that adds what the source never said,
has failed.

## 1. Set the target first

| Decide | Options |
|---|---|
| Reader and purpose | Decide something, learn the topic, check a requirement, prepare a meeting |
| Focus | Whole document, or one question ("what are the supplier's obligations?") |
| Length | One line, one paragraph, one page, or a section-by-section digest |
| Form | Prose brief, bullet list, table, action list |

If the user gave no target, deliver the default layered summary in section 4. It serves most readers.

## 2. Read all of it, in order

Never summarize from the first pages, the abstract or the contents page alone. Conclusions, exceptions and
numbers that change the meaning are often in the middle or the appendix.

1. **Survey**: page count, contents page, headings. This gives the map and the section boundaries.
2. **Read in parts**: long PDFs are read in page ranges (for example 20 pages at a time) until the last
   page. Record which ranges were read.
3. **Take notes per section** as you go, each note with its page number:
   - the main claim or purpose of the section
   - facts, figures, dates, names, limits and conditions (copied exactly, with units)
   - decisions, requirements, obligations, deadlines
   - exceptions, risks, open questions
4. **Scanned or picture-only pages**: look at the page image; if the text cannot be read, say which pages
   were not covered.
5. **Tables and charts** carry key data. Read them, do not skip them.

For several documents, note each one separately first, then compare.

## 3. Build the summary from the notes

- **Group by meaning, not by page order**, when that serves the reader better: findings together, costs
  together, actions together.
- **Weight by importance, not by length in the source.** Ten pages of background may deserve one line; one
  sentence with a deadline or a penalty deserves its own line.
- **Keep the numbers.** `Cost rose 12% to 4.3 M USD in 2025` is useful; `costs increased significantly` is not.
- **Keep conditions with their claims**: `approved, provided the test passes by 30 June` must not become `approved`.
- **Use the source's own terms** for defined words, product names and legal language.
- **Separate three things clearly**: what the source says, what it does not say, and your own comment.
  Your comment is added only if useful, and is labelled.
- **When sources disagree**, show both positions with their references; do not average them.

## 4. Default output: layered summary

```
# <Document title> - Summary
Source: <file name>, <pages> pages, <date/version>. Pages read: all.

## In one sentence
<The whole document in one sentence.>

## Key points
- <5 to 7 points, most important first, each with a page reference (p. 12)>

## Section digest
| Section | What it says | Pages |
|---|---|---|

## Numbers and dates that matter
| Item | Value | Page |
|---|---|---|

## Decisions, requirements and actions
| What | Who | When | Page |
|---|---|---|---|

## Open questions and gaps
- <What the document leaves unclear, missing or contradictory>
```

Drop a part that has nothing in it. Scale the rest: a 10-page document needs half a page, a 300-page manual
may need two.

Other forms on request:

| Request | Form |
|---|---|
| "Brief me for a meeting" | One paragraph, three talking points, questions to ask |
| "What does it say about X" | Direct answer first, then every relevant passage with page numbers, and "not covered" if so |
| "Compare A and B" | Table: topic, A, B, difference; then the conclusion |
| Big topic with no document | Definition, why it matters, main parts, how they connect, current state, what is debated |
| Meeting transcript or email thread | Decisions, actions (owner, date), open points; who said what only where it matters |

## 5. Fidelity check

Before delivering, test the summary against the notes and the source:

1. Every number, date and name matches the source exactly.
2. Every key point has a page reference that really contains it.
3. Nothing in the summary is absent from the source (except labelled comments).
4. The conclusion and the main limits or risks of the source are both present.
5. A person who reads only the summary would not be misled about what the source decides or requires.
6. Coverage is stated honestly: all pages read, or which pages were not and why.

## 6. Style

Plain words, short sentences, the reader's language level. Explain a technical term in a few words the
first time. No opinion words (`excellent`, `unfortunately`) unless they are the source's own.
