# Network Tank Arena

This project extends the provided tank arena to improve client movement under
latency and packet loss. The focus is movement responsiveness and server corrections.

## How to Run

1. Open the project with *Unity 6000.3.26f1 and load `Assets/Scenes/SampleScene.unity`.
2. Build that scene as a standalone player, then run it alongside the Editor in Play Mode.
3. Click Start Host in the build and Start Client (localhost) in the Editor.
4. W/S moves, A/D turns, and the mouse aims. Focus the window you want to
   control and release movement keys after switching windows. Firing is disabled.
5. The client's Correction min slider controls the minimum correction time
   from 0.05 to 0.20 seconds, default 0.10. Larger corrections can take longer.

Stop the Client before the Host using **Stop session**.

## The Changes

- The owning client moves immediately using local input, then accepts server corrections.
  Local movement runs every rendered frame. There is no input-history replay.
- Small corrections are spread over time and limited by the tank's movement and turn
  speeds to reduce backward kicks. The in-game slider makes this easier to compare.
- The server still decides the accepted movement and turret state. Clients send input,
  not authoritative positions. Other players receive replicated state with smoothing.

## A problem found and fixed

The client was slower even on the server: measured movement was about 3.5–5 units per second,
instead of the hosts 6 units per second. Input receipt and expiry used `Time.unscaledTimeAsDouble`,
which returns a different time inside `FixedUpdate`. Fresh input could appear to be
from the future and be rejected for a physics step.

Both checks now use `Time.realtimeSinceStartupAsDouble`. After the fix, the server moved
both tanks at approximately 6 units per second in all 12 measured trials.

## Testing

Added delay each way + Packet Loss each way
0 ms, 0% pl
50 ms, 0& pl
150 ms, 0% pl
150 ms, 10% pl

To reproduce the network stuff, create a preset through **Assets > Create > Multiplayer >
NetworkSimulatorPresetAsset**. In the client Editor's Play Mode, before connecting,
add a **NetworkSimulator** component to a scene GameObject. Choose **Custom** under
**Connection Preset** and assign the preset. Set **Packet Delay (ms)** and **Packet Loss (%)**
from the table, with **Packet Jitter (ms)** and **Packet Loss Interval** at zero.
Keep the Host unimpaired: the client simulator affects sending and receiving, so
150 ms each way adds roughly 300 ms round-trip delay.

Repeat straight / turning movements, stops and turns while watching both instances and allowing them
to settle!

## Remaining limitations and known issues

Movement felt nicer overall, especially at 50 ms, and less jumpy at 150 ms with 10%
loss. However, it still feels sluggish. Delayed server corrections can slow or
pause local movement. Smoothing hides some disruption but does not remove packet loss
or network delay. Errors above 3 units or 45 degrees still snap to the server
state.

Tank barrel jitters, which I felt was outside of my scope-of-time to improve.