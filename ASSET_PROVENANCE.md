# Last King asset sources

- `LastKingRoom.png`: created with the built-in image generation tool for this project. Dark pixel-art industrial chamber, fixed first-person seated camera, overhead warm lamp, wooden tabletop in the foreground, empty opponent position, muted bronze/black palette. No UI, cards, coins, characters or balance scale baked into the background.
- `OpponentChair.png`: created with the built-in image generation tool, then edited with the same tool to remove wrist restraints. Isolated seated shadowed opponent on a vintage electric chair, head cap/cables, matching bronze pixel-art palette, transparent background. Both wrists and hands must remain free so the character can play cards. The earlier restrained-wrist version is not used.
- `CardAtlas.png`: copied from the existing project's `Assets/RevolverMVP/Resources/Art/cards.png`; existing King/Citizen/Slave artwork reused. The three card sprites are separate import regions of this atlas.
- Simple scale/coin/UI shape textures and short synthesized sound effects are generated locally by the scene setup utility and saved as Unity project assets. They carry no external Asset Store dependency.

The paragraphs above record the final prompt specifications for the generated room and corrected character. Tool mode: built-in image generation (not CLI/API fallback). Generated files have been copied into `Assets/TheLastKing/Art`; project references do not depend on Codex's generated-image cache.
