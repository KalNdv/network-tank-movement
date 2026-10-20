(Taken from original)
# Network Tank Arena

A two-player, server-authoritative movement and turret-aim starter for the first
Network Programming lab.

Use **Unity 6.3.26f1 (6000.3.26f1)**. In Unity Hub, choose **Add project from disk**
and select this repository folder, then open `Assets/Scenes/SampleScene.unity`.
The project already includes Netcode for GameObjects 2.13.3 and Unity Transport
2.7.4. The multiplayer components require the following one-time Editor setup.

## Start the lab

1. Enter Play Mode in two instances using the same saved scene and prefab.
2. In the first Game view, click **Start Host**.
3. In the second Game view, click **Start Client (localhost)**.
4. Each instance controls its own tank: **W/S** moves forward/backward, **A/D**
   turns the chassis, and the **mouse** aims the turret independently. Click/focus
   each Game view before controlling it. Release movement keys after changing
   focus or joining a session. Left mouse firing is deliberately disabled here.
5. Use **Stop session** to disconnect. Wait for shutdown to finish before starting
   again. Stop the Client before the Host when ending a normal lab run.

The server simulates both tanks. Players send intent; they do not write final
positions or turret state. Each instance has one camera following its own tank.
The baseline supports two connected players and replicates movement and turret
aim. Movement has visible latency because prediction is intentionally absent.
