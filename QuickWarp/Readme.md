# Silksong Quick Warp

A simple BepInEx mod that lets you save and load custom waypoints in **Hollow Knight: Silksong**.

---

## Usage

- **Save a waypoint:** Press **F6**  
- **Load a waypoint:** Press **F7**

### Examples
- Save the game, press **F6**, and when you load the game again (bench respawn), press **F7** to teleport back to your last position.
- Save right in front of a boss so if you die you can skip the walk back.
- Practice or train tricky sections quickly.
- Etc.

---

## ⚠️ Important Notes

This mod does **not** save or restore the game state — it only moves your character’s position.  
Some behaviors may break the game logic:

- Sitting on a bench and pressing **F7** → You will float (since sitting disables normal physics).
- Saving in the middle of a boss fight → Undefined behavior (boss fight may not trigger properly when you warp back).
- Other scripted events may also desync.

👉 **Use at your own risk.**

---

## Installation

1. Install **BepInEx**: [https://github.com/BepInEx/BepInEx/releases](https://github.com/BepInEx/BepInEx/releases)  
2. Download this mod.  
3. Unzip the content into the game’s root folder (the same place where the `.exe` is located).  
   - The file structure should look like:  
     ```
     <GameFolder>/
       BepInEx/
         plugins/
           QuickWarp/
             QuickWarp.dll
     ```

---

## License

Free to use, share, and modify.  
Not affiliated with Team Cherry.
