# Treasure Cave sample

`TextAdventure.cmideproj` is a complete, monochrome Macintosh SE project. It
uses three short text fields and four buttons to teach stable control IDs,
`OnClick` bindings, application state, and simple conditional logic. The game
is deterministic: take the key before opening the chest, then start over to
repeat the story.

Open the project in ClassicMacStudio and choose **Build & Test** with
**Mini vMac (Mac SE)**. The generated test checks widget interaction and
dispatches each bound button handler. Use **Build & Run** to play the intended
sequence and check the visible story and inventory changes. Interactive
testing in Mini vMac verified the locked state, key pickup, duplicate pickup,
victory, repeated actions, look descriptions, and reset behavior.

The test/manual section in the matching beginner guide adds explicit
state-transition checks; physical Macintosh compatibility has not been
verified.
