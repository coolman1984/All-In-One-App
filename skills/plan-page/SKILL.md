---
name: plan-page
description: Before building a large or risky change to All in One (data safety, security, a new feature across several files), write the plan as a short blog-style web page, publish it for the owner, and build only after approval. Use when the owner asks for a plan, asks "do you have a good feel for a path here?", or when a change touches saved data, security or several pages.
---

# Plan page first, then build

The owner reads the plan as a page, not as chat text. The page is the review step.

## 1. Check the path first

- Read the relevant source and the open items in [HISTORY.md](../../HISTORY.md). Confirm the problem in the code and cite `file:line`.
- If you are not confident about the path yet, say what is unclear and investigate before writing.

## 2. Write the page like a blog post

Use this order. Each part answers one question.

| Part | Question it answers |
|---|---|
| Header pill | Plan name and revision number (Rev 1, Rev 2 …) |
| Title + story | What goes wrong for a real user today, told as a short scene |
| Four summary cards | Default behavior, the main promise, history kept, the limit that matters |
| Workflow | What the app will do, step by step, in user words |
| Architecture | Where in the code it changes and how |
| Design traps | Easy mistakes and why we avoid them |
| Contracts and limits | The exact numbers and rules we commit to, plus what is out of scope |
| What you will see | The message, button or settings place the user meets |
| Tests and phases | How we prove it works, and the order of delivery |

## 3. Writing rules

- Write for a non-technical owner. The owner reads Egyptian Arabic. Put code paths only in a small references line.
- No invented jargon or labels. Every sentence moves the plan forward.
- Do not invent rules that block or delay the work. If one of your own rules gets in the way, fix it and continue.
- The final chat reply must make sense alone: one line plus the link.

## 4. Publish, review, build

- Keep the source in `docs/plans/<name>.html` and publish it as an artifact. Use the app's Flow palette (`#2F66F6` accent, `#F5F7FC` canvas).
- Wait for approval. Each round of changes raises the revision number on the page.
- After approval, build in the order of the phases on the page and record each phase in HISTORY.md.

Example: [never lose a note](../../docs/plans/never-lose-a-note.html).
