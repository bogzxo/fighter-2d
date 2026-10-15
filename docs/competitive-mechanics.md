# What a competitive fighter has that Fighter2D didn't, and how it gets them

The movement went Smash on 2026-10-15 (see the "How a fight moves" bit of CLAUDE.md). This is the next lot, how a hit
lands and when you get to do something again after throwing one, worked out from Smash Ultimate, Rivals of Aether,
Street Fighter and MUGEN. The ones marked built are in, the rest are written down in the order they'd go.

## What the games do

Smash Ultimate (SmashWiki's Hitlag, DI, SDI, Knockback and Stale-move negation pages). Both fighters freeze when a hit
lands, the hitlag, `floor(damage * 0.65 + 6)` frames, never more than 30, a shield takes 0.67 of it. The victim is
launched when it ends, not when the hit lands, and the stick held on that last frame of hitlag turns the launch by up to
0.17 radians (9.74°) one way or the other, most from a direction square on to the launch, DI. During the hitlag every
new direction pressed shoves the victim a little that way (2 units a press, a press counted every 4 frames at most),
SDI, which is how somebody wriggles out of a multi hit. Knockback grows with how much damage they have taken, a fixed
base plus a scaling times their percent, and weight takes some of it off. Somebody knocked down who presses shield in
the 11 frames before they hit the floor techs, gets up on the spot or rolls away with i-frames instead of lying there.
The same move used over and over does less, the last 9 moves that landed are remembered and each copy of this one in
there takes 9%, 8%, 7% and so on off. When you can act again is the move's interruptible frame (IASA), landing lag for
aerials, and a jump cancel on a few moves.

Rivals of Aether. Windows with frames and the art spread over them, base knockback and knockback scaling per hitbox,
hitpause that grows with damage, `hitstun_grav` on every character (about half), whifflag, extra endlag on a move that
missed so throwing things out costs, and parry, block at the right moment and the attacker is the one stuck.

Street Fighter. Normals cancel into specials and specials into supers on hit or block, the cancel hierarchy, plus
chains and target combos for normals into normals. Links are moves that are plus enough to connect without a cancel.
Perfect parry in SF6 and Third Strike's parry are the timed block.

MUGEN. `ground.hittime`, `air.hittime`, `yaccel` per hit, juggle points, which is what Fighter2D's exact hitstun and
`hitstun_gravity` already are.

## What Fighter2D gets

Built (2026-10-15, each checked in a scripted fight), in this order.

1. Hitlag the Smash way. Both freeze, the victim where they were hit, the launch (and any shove) waits for the end of
   it. The usual length is `floor(damage * 0.65 + 6)`, at most 30, a block 0.67 of that, a counter hit 4 frames more,
   the K.O. 36. A move still says `hitstop` to have its own.
2. DI. The direction the victim holds when the hitlag ends turns a launch by up to 9.74°, as much as the direction is
   square on to it.
3. SDI. Every direction pressed during the hitlag, one every 4 frames at most, shoves the victim 4 units that way
   (Smash's 2, Fighter2D's fighters are about twice the size in units), through the physics so nobody ends up in a wall.
4. Knockback that grows. `launch_growth: vec(away, up)` on a move is how much further it sends somebody for every 10
   health they've lost, `weight` on a character (100 the usual) takes that much less of the growth. At full health a
   launch is exactly `launch`, so the numbers in a file still mean what they say.
5. Teching. Somebody launched by a knockdown who presses block in the 11 frames before they land doesn't go down, they
   get up on the spot (`tech`) or, holding a direction, roll that way (`tech_roll`), both with i-frames. Missing it
   locks teching out for 40 frames.
6. Kinds and the cancel hierarchy. A move is a `kind`, "normal", "special" or "super" (a move that costs meter is a
   special unless it says, half a meter or more a super), and `hit_cancel` takes the kinds as well as names, so
   `hit_cancel: ["special", "super"]` on a normal is Street Fighter's cancel.
7. Whiff lag. `whiff_lag: N` on a move that didn't land, its cancel frames don't count and it holds its last frame N
   frames longer. Rivals does it so swinging at nothing costs.
8. Stale moves. The last 9 moves that landed (each player's own), every copy of this one in there takes 9, 8, 7, ... 1%
   of its damage off, a fresh move hits full. Over at the end of the round.
9. Parry. Block started 5 frames or fewer before the hit lands it as a parry, no chip, no blockstun and no shove, and
   the attacker is stuck for 20 frames. Called out as PARRY.

Later, each its own piece of work.

- Hex's move editor learns all of the above (the schema, the rules copy, the combo window showing DI's cone).
- Air dodge, Smash's escape in the air, and a hitstun cancel into it after the launch's own hitstun runs long.
- Grabs and throws, the answer to blocking, which want art the characters haven't got.
- Ledges, if a map ever has an edge to fall off.
- Juggle points (MUGEN) if the gravity growth turns out not to be enough to stop a loop.
- The dummy learning to DI, tech and parry, from the training menu.
