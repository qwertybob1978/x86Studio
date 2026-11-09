# Tiny Notepad sample

`Notepad.cmideproj` is a complete WYSIWYG project for the monochrome
Macintosh SE profile. Its form has an editable note field plus Save, Load, and
Clear buttons. Bound handlers in [`src`](src/) store plain text as
`Note.txt` beside the running application on the same HFS volume. The sample
keeps notes to the classic Pascal-string limit of 255 bytes.

Open the project in ClassicMacStudio and use **Build & Test** with
**Mini vMac (Mac SE)**. The generated widget test clicks each button and
verifies generated control metadata and bound click dispatch; it does not
assert the file operations. In an interactive Mini vMac run, saving
`HELLO MAC`, clearing the field, and loading the note again were verified.
The emulator image is written to `.build` and is reproducible from the
checked-in project and C sources.

Physical Macintosh testing is not claimed. For the procedure and safe
MacBinary/Agent transfer notes, see the three beginner manuals in
`tutorial/guides/` and the [Serial Agent connection
manual](../../serial-agent-manual.md).
