---
name: powerpoint-design
description: Design and organize clear, professional PowerPoint decks - storyline, one message per slide, action titles, grid layout, type and colour system, charts and tables, and a visual check of every slide. Use when the user asks to make, redesign, tidy, shorten or review a presentation, slides or a deck, or to turn a report, notes or data into slides - even if they only say "make it look better".
---

# PowerPoint design and organization

Goal: a deck a busy manager understands by reading only the titles, and that looks like one hand made it.
Design here means order and clarity, not decoration.

## 1. Story before slides

Write the storyline as plain text first. Do not open PowerPoint until it reads well.

1. **Audience and decision**: who reads it, and what should they decide or do after?
2. **One-sentence answer**: the whole deck in one sentence. If this is hard, the content is not ready.
3. **Title list**: one line per slide. Read top to bottom, the titles alone must tell the full story.

Default order for a business deck:

| Part | Slides | Content |
|---|---|---|
| Opening | 1 | Title, date, owner |
| Summary | 1 | The answer, 3 supporting points, what is asked of the audience |
| Body | 3-10 | One supporting point per section: situation, problem, analysis, solution, result |
| Close | 1 | Next steps with owner and date |
| Appendix | any | Detail tables, method, backup numbers |

Put the conclusion first. Leaders may stop after slide 2; it must be enough.

## 2. One slide, one message

- **Action title**: a full sentence stating the finding, not a topic.
  Weak: `Defect rate`. Strong: `Defect rate fell 40% after the new jig in March`.
- Everything on the slide proves the title. Anything else moves to another slide or the appendix.
- Title is at most two lines. Body is at most about 40 words or one chart or one table.
- Too much for one slide means two slides, never a smaller font.

## 3. Layout system

Use the same frame on every slide so the eye learns where things are.

| Element | Value (16:9 slide, 13.33 x 7.5 in) |
|---|---|
| Margins | 0.5 in on all sides; nothing crosses them |
| Title | top-left, same position and size on every slide |
| Body area | one zone, or two or three equal columns with 0.3 in gaps |
| Footer | page number, source, confidentiality mark; small and grey |
| Alignment | every object shares an edge with another object; equal spacing between equals |

Pick the layout by content:

| Content | Layout |
|---|---|
| One key number | Big number (60-96 pt) with one line of meaning under it |
| Comparison | Two columns, same structure on both sides |
| Process or timeline | Left-to-right steps, 3 to 5, equal boxes |
| Trend or ranking | One chart on two thirds, takeaway text on one third |
| Detail data | Table with at most about 6 columns and 8 rows; highlight the row that matters |
| Section start | Section title only |

Use the template's own slide layouts and placeholders, not loose text boxes. Placeholders keep position,
font and colour consistent and survive a template change.

## 4. Type and colour

- **Font**: one family for the whole deck (the company template font, else Calibri or Segoe UI).
- **Sizes**: title 28-32 pt, body 16-20 pt, labels and footnotes 12 pt minimum. If it cannot be read from
  the back of the room, it is too small.
- **Colour**: one main colour, one accent, and greys. The accent is for the single thing to notice.
  Red and green mean bad and good only.
- **Contrast**: dark text on light background or the reverse. No text over a busy picture.
- **Emphasis**: bold or accent colour, on a few words only. No underline, no all-capitals body text.
- Left-align body text. Centre only titles of title slides and single big numbers.

## 5. Charts, tables, pictures

- The chart title or slide title says the conclusion; the chart shows it.
- Line for change over time, bars for comparison, stacked bar for parts of a whole, table for exact values.
  Avoid pie charts with more than three parts and all 3-D effects.
- Remove what carries no information: gridlines, borders, legends that can be direct labels.
- Grey for context, accent for the series being discussed. Label the important value directly.
- Give the unit and the source under every chart and table.
- Pictures: sharp, cropped to the subject, same shape and size when shown in a set.

## 6. Building with PowerPoint (COM, no extra packages)

```powershell
$pp = New-Object -ComObject PowerPoint.Application
try {
    $pres = $pp.Presentations.Open($templatePath, -1, 0, 0)       # read-only, no window
    $layout = $pres.SlideMaster.CustomLayouts.Item(2)             # look up the layout names first
    $slide = $pres.Slides.AddSlide($pres.Slides.Count + 1, $layout)
    $slide.Shapes.Item(1).TextFrame.TextRange.Text = 'Defect rate fell 40% after the new jig in March'
    $pres.SaveCopyAs($outPath)                                    # never save over the template
    $pres.Slides.Item(1).Export($pngPath, 'PNG', 1920, 1080)      # picture for the visual check
}
finally { if ($pres) { $pres.Close() }; $pp.Quit() }
```

Positions and sizes are in points (72 per inch; a 16:9 slide is 960 x 540). List the layouts and the
placeholders of each layout before filling them; their order differs between templates.

## 7. Check every slide by looking at it

Export each slide to PNG and view the picture. Reading the text alone misses overflow and overlap.

1. Titles only, in order: is the story complete and logical?
2. No text cut off, overlapping, or outside the margins.
3. Same title position, fonts, colours and footer on every slide.
4. Every number has a unit, a period and a source, and agrees with the same number on other slides.
5. Spelling, dates and names are correct.
6. The summary slide still matches the body after the last edit.

Fix what the pictures show, export again, and only then deliver. Report the file path, the slide count and
anything left for the user to supply (missing data, pictures, approvals).
