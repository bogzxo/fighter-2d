# The UI and UX revamp

Written 2026-10-16 off screenshots of every screen as it is (main menu, options, fight setup, the fight, the pause, the
drawer). The flow underneath is right and stays, `MenuActions`, the hint bar, overlays rather than rebuilds, the seats
of the setup screen. What's wrong is how it looks and where things sit.

## What's wrong with it

1. **The setup screen hides its own map.** Three framed panels (player one, the roster with the rules under it, player
   two) cover about four fifths of the screen, and the live map behind them, the whole point of the preview, shows in
   slivers between them. The map is the one thing on that screen that changes the most and it's the one you can't see.
2. **Everything is a framed box.** The dragon frame (gold corners, purple fill) is on the menu, the options, every
   corner of the setup screen, the pause, the drawer and the result card. When everything is ornate nothing stands out,
   and the frames eat room the text then hasn't got.
3. **The text is tiny.** Most labels are 0.2 to 0.3 of Klaxon at 1600 by 900, the hints and the stat names read like
   footnotes on a laptop and not at all from a sofa, which is where a fighting game is played.
4. **The colours fight each other.** Red buttons, purple panels, a lavender forest behind the main menu, cyan for the
   selection, the Japan map's indigo and pink behind the setup screen. Nothing says what's selected the same way twice
   (the main menu's selected button is cyan, the setup's selected cell has gold corners, the options row is lighter).
5. **The hints float.** The line of what the buttons do is at the bottom of the screen, a long way under the box it is
   about (the options), in grey on whatever is behind it.
6. **The rules are in the middle of picking.** Map, rounds, time and weather sit under the roster while two people are
   still choosing fighters, and only player one can change them once everybody is locked in, so for most of the time
   they're four rows nobody can touch.
7. **Small things.** A black frame while the setup screen's map loads under the transition. The main menu's line saying
   what a button does is squeezed under the buttons in the panel. The options' description panel is mostly empty.

## Where it should end up

One look across every screen, which is the job, more than any one screen.

- **Panels are glass, frames are for what matters.** Menus sit on a dark translucent panel with a thin edge in the
  accent, no ornate frame, so the world behind shows through. The ornate frame is kept for one thing a screen, the
  focal box (the result card, the versus plates).
- **One way to say "selected".** The accent colour behind the selected row or cell, everywhere, with the same pulse.
- **Bigger type, fewer words.** A floor of about 0.3 for anything read, 0.45 and up for what's picked, the hints on
  a bar of their own at the foot of the screen with a backing so they read over anything.
- **The setup screen as a fighting game does it.** The map fills the screen. Each fighter stands big on their own side
  with their name, their stat bars and their blurb on a strip of glass, no box round them. The roster is a compact grid
  in the middle low down. The rules go to a stage select of their own once everybody is locked in (see the questions),
  where the map is the screen and the four rules are a slim bar across the bottom.
- **The main menu** as a column of big text buttons down the left with the selected one lit, the line of what it does
  next to it rather than under it, the duel behind it on a map that suits the game.
- **The options** wider and calmer, the tabs across the top as text, rows twice as tall, the description beside the
  row that's selected.
- **The fight's overlays** (the pause, the drawer, the result card, the controls card) in the same glass.

## Art

The dragon skin is CC0 off OpenGameArt and is pixel art, the fighters and maps are pixel art too, so whatever comes in
stays pixel art at its own density (the rule from the skins round). What's wanted from a pack is a glass panel, a thin
edged button, a selection highlight, a tab and a slim slider, not more frames. The places to look are itch.io (free
fighting game and pixel UI packs, licence checked one by one), OpenGameArt and kenney.nl (CC0, the Pixel UI pack). A
pack comes in as a skin of its own under `Horizon/Assets/uix` with its README, cut by `Art/make_ui_packs.py`, so the
dragon skin and the rest stay selectable in the options. A thing no pack has (a glass panel is a rounded rectangle) is
drawn by the engine (`Panel.Color` with `Radius`) rather than painted.

## The order to do it in

Each one a screen or two, checked with a screenshot of the screen walked to on the keys, the layouts edited the way
Hex reads them.

1. The look, the skin and its colours, the glass panel, the selection, the type sizes, the hint bar.
2. The setup screen, and the stage select if that's the call.
3. The main menu.
4. The options and the bindings.
5. The pause, the drawer, the result card, the controls card, the join screen.
6. The black frame under the setup screen's transition.

## Decided with bogz (2026-10-16)

- **A stage select.** The fighters are picked on a screen of their own, then the map fills the screen with the map,
  the rounds, the time and the weather on a slim bar across the bottom.
- **A new pixel pack off the internet**, a skin of its own with glass panels and made the default, the dragon skin
  staying in the options.
- **Everything, the HUD too.** The menus, the fight's overlays, and the health bars, the clock, the meter and the calls.
- **The live Japan map behind the main menu**, the duel fighting on it, so the menus look like the game.
