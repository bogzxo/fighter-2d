-- Makes an .ase file out of a sprite sheet. Run by build_ase.py, which writes the manifest that says which cell of the sheet
-- is which frame and where the tags go:
--   Aseprite -b --script-param manifest=<file> --script build_ase.lua

local manifest = dofile(app.params["manifest"])

local sheet = Image{ fromFile = manifest.sheet }
if sheet == nil then
  error("Couldn't open the sheet " .. manifest.sheet)
end

local width, height = manifest.cell_width, manifest.cell_height

local sprite = Sprite(width, height, ColorMode.RGB)
sprite.filename = manifest.output

local layer = sprite.layers[1]
layer.name = manifest.name

app.transaction(function()
  for index, cell in ipairs(manifest.frames) do
    -- A new sprite comes with its first frame
    local frame = index == 1 and sprite.frames[1] or sprite:newEmptyFrame()
    frame.duration = manifest.seconds

    local x = (cell % manifest.columns) * width
    local y = math.floor(cell / manifest.columns) * height

    local whole = Image(width, height, ColorMode.RGB)
    whole:drawImage(sheet, Point(-x, -y), 255, BlendMode.SRC)

    -- Only the part with something drawn on it is kept, which is what Aseprite does with its own cels
    local bounds = whole:shrinkBounds()
    if not bounds.isEmpty then
      -- The new sprite already has an empty cel on its first frame
      local old = layer:cel(frame)
      if old ~= nil then sprite:deleteCel(old) end

      sprite:newCel(layer, frame, Image(whole, bounds), Point(bounds.x, bounds.y))
    end
  end

  for _, wanted in ipairs(manifest.tags) do
    local tag = sprite:newTag(wanted.from, wanted.to)
    tag.name = wanted.name
    tag.aniDir = wanted.direction == "reverse" and AniDir.REVERSE or AniDir.FORWARD

    -- 0 goes round for ever, 1 plays once
    tag.repeats = wanted.loop and 0 or 1
  end
end)

sprite:saveAs(manifest.output)
