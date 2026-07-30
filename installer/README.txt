SONS OF THE FOREST - LIVE EDITOR
====================================================================

HOW TO INSTALL

  Run  INSTALL.bat

That is all of it. No prompts, no admin rights.

The installer will:
  - find your game on any drive (via Steam's registry entry and its
    library list, so it takes about 50 ms)
  - install RedLoader, if you don't already have it
  - put the mod in  <game>\Mods\LiveEditor.dll
  - put the app in  <game>\LiveEditorApp\
  - make a Desktop shortcut called "SOTF Live Editor"

Close the game before running it.


HOW TO USE

  1. Start Sons Of The Forest and load a save.
  2. Open "SOTF Live Editor" from your Desktop.

It connects on its own. Everything you change applies to the running
game immediately - no saving, no reloading.

In multiplayer you need to be the host, or have cheats enabled.


IF IT CAN'T FIND YOUR GAME

Tell it where the game is and run INSTALL.bat again:

  setx SOTF_GAME_DIR "D:\Games\Sons Of The Forest"

The app has its own override too: put the folder path in a file called
gamedir.txt next to LiveEditorApp.exe.


UNINSTALL

Delete these from your game folder:
  Mods\LiveEditor.dll
  Mods\LiveEditor\
  LiveEditorApp\

Nothing is ever written to your save files, so your saves are
unaffected either way.


WHAT'S IN THE BOX

  INSTALL.bat   the installer
  mod\          the in-game plugin (no GUI - it draws nothing on screen)
  app\          the control window (self-contained, no .NET install needed)


Source, full documentation and the item ID / console command reference:
https://github.com/Nitro70/SonsOfTheForest-LiveEditor

MIT licensed. Unofficial and fan-made; not affiliated with Endnight Games.
