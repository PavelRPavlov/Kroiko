# Issue tracker

> Repo-scoped tracker configuration for agents (applies to **this repository only**).

- **Tracker:** GitHub Issues on [PavelRPavlov/Kroiko](https://github.com/PavelRPavlov/Kroiko/issues).
- **Board:** every issue an agent creates is also added to the GitHub Project
  [PavelRPavlov / projects / 3](https://github.com/users/PavelRPavlov/projects/3).
- **Do not** fall back to local markdown tickets — the GitHub repo is the tracker.
- **CLI:** `gh` (needs scopes `repo` and `project`; if `project` is missing, the human runs
  `gh auth refresh -s project` — agents never run auth flows themselves).
- The repo is **public**: never put secrets, customer data, or real Polyboard files in issues.

Shell snippets below are Git Bash. `R=PavelRPavlov/Kroiko`.

## Wayfinding operations

Used by the `/wayfinder` skill.

| Concept | How it's expressed here |
|---|---|
| **Map** | One issue labelled `wayfinder:map`. Body = Destination / Notes / Decisions so far / Not yet specified / Out of scope. |
| **Ticket** | An issue labelled `wayfinder:<type>` (`research`, `prototype`, `grilling`, `task`), attached to the map as a **native sub-issue**. |
| **Claim** | Assign the ticket to the driving dev (`gh issue edit N --add-assignee @me`) **before** any work. |
| **Blocking** | GitHub's **native issue dependencies** ("blocked by"), so the frontier shows in the UI. |
| **Resolution** | Post the answer as a comment, close the issue, append a line to the map's *Decisions so far*. |
| **Board** | Add the map and every ticket to Project 3. |

### Labels (create once)

```bash
gh label create wayfinder:map       -R $R -c 5319e7 -d "Wayfinder map (index issue)"
gh label create wayfinder:research  -R $R -c 0e8a16 -d "Wayfinder ticket: research (AFK)"
gh label create wayfinder:prototype -R $R -c fbca04 -d "Wayfinder ticket: prototype (HITL)"
gh label create wayfinder:grilling  -R $R -c d93f0b -d "Wayfinder ticket: grilling (HITL)"
gh label create wayfinder:task      -R $R -c 1d76db -d "Wayfinder ticket: task"
```

### Create a ticket and attach it to the map

Sub-issue and dependency APIs take the issue's numeric **database id**, not its number.

```bash
url=$(gh issue create -R $R -t "<title>" -l wayfinder:grilling -F body.md)
num=${url##*/}
id=$(gh api repos/$R/issues/$num -q .id)
gh api -X POST repos/$R/issues/$MAP/sub_issues -F sub_issue_id=$id
gh project item-add 3 --owner PavelRPavlov --url "$url"
```

### Wire blocking (second pass, once all ids exist)

```bash
# ticket $B is blocked by ticket $A
a_id=$(gh api repos/$R/issues/$A -q .id)
gh api -X POST repos/$R/issues/$B/dependencies/blocked_by -F issue_id=$a_id
```

### Frontier query

Open, unassigned children of the map whose blockers are all closed:

```bash
for n in $(gh api repos/$R/issues/$MAP/sub_issues --paginate \
             -q '.[] | select(.state=="open" and (.assignees|length)==0) | .number'); do
  open_blockers=$(gh api repos/$R/issues/$n/dependencies/blocked_by -q '[.[] | select(.state=="open")] | length')
  [ "$open_blockers" = 0 ] && gh issue view $n -R $R --json number,title,labels -q '"\(.number)\t\(.title)"'
done
```

### Record a resolution

```bash
gh issue comment $N -R $R -F resolution.md
gh issue close   $N -R $R --reason completed
# then edit the map body: append "- [<title>](<url>) — <gist>" under Decisions so far
gh issue edit $MAP -R $R -F map.md
```

Out-of-scope tickets: close with `--reason "not planned"` and add a line under the map's *Out of scope*.
