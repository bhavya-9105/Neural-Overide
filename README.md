# NEURAL OVERRIDE (Puppet Protocol)

> **A fast-paced, high-performance 2D tactical survival game built with Unity URP. Neutralize rogue AI nodes, hijack hostile units to turn enemy firepower against the swarm, and survive timed hostile incursions.**

---

## 🎮 Core Gameplay Mechanics

- **Left-Click (Pulse Raycast Beam)**: Precision raycast strike with screen recoil and dynamic audio feedback cues. Instantly vaporizes low-tier targets and applies heavy damage along the targeting trajectory.
- **Right-Click (Neural Tether / Puppet Protocol)**: Slave rogue enemy nodes, temporarily converting them into defensive minions with pulsing tether links before triggering a high-damage overload detonation.
- **Right-Click / Overclock (EMP Burst)**: Field-wide wipe ability operating on a tactical cooldown timer, vaporizing all active hostile units and granting emergency breathing room.
- **Tiered Enemy Units**:
  - **Chaser Nodes**: Relentless, rapid melee pursuit units designed to swarm and box in the player.
  - **Sniper Nodes**: Evasive ranged attackers with predictive targeting and high-velocity projectile trajectories.
- **Progressive Difficulty Protocol**:
  - **Level 1 (Easy)**: 20s survival benchmark, manageable spawn cadences.
  - **Level 2 (Normal)**: 50s survival benchmark, combined melee/ranged pressure.
  - **Level 3 (Expert)**: 90s survival benchmark, dense swarms and aggressive sniper waves.

---

## 🕹️ Controls

| Action | Input Binding |
| :--- | :--- |
| **Move** | `W` `A` `S` `D` / `Arrow Keys` |
| **Aim / Fire Laser** | `Mouse Cursor` + `Left-Click` |
| **Puppet Tether / EMP** | `Right-Click` |
| **Pause / Menu** | `ESC` |
| **Quick Restart** | `R` |

---

## 🛠️ Tech Stack & Architecture

- **Engine**: Unity (Universal Render Pipeline / URP 2D)
- **Code**: Modular C# with decoupled controllers, singletons, and event-driven game state management (`GameManager`, `PlayerController`, `WaveSpawner`, `NeuralTether`, `EMPAbility`).
- **UI System**: Vector TextMeshPro rendering strictly calibrated and scaled to native 1080p with zero downsampling or blur.
- **Collision & Physics**: Physical perimeter colliders, hard arena coordinate clamping, continuous 2D collision detection, and zero-latency raycast validation.

---

## 🚀 Setup & Installation

1. **Clone the repository**:
   ```bash
   git clone <repo-url>
   ```
2. **Open project via Unity Hub**:
   - Launch Unity Hub.
   - Select **Add** > **Add project from disk** and browse to the repository root directory.
   - Open with a compatible Unity version (compatible with current Unity LTS / URP).
3. **Open the Scene**:
   - In the Project window, open `Assets/Scenes/MainScene.unity` (or `Assets/Scenes/SampleScene.unity`).
4. **Generate / Play**:
   - Select **GameJam > Setup Scene & Sprites** from the top menu bar (if regenerating the layout or rebuilding sprites and UI).
   - Press **Play** in the Unity Editor to enter the arena.
