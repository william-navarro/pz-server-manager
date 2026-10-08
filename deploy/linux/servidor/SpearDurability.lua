-- Optional server-side example: doubles spear durability (ConditionLowerChanceOneIn x2).
-- Project Zomboid calculates weapon wear on the server; this example requires DoLuaChecksum=false.
local MULTIPLIER = 2
local originals = {}

local function isSpear(item)
    local name = item:getName() or ""
    return string.sub(name, 1, 5) == "Spear"
end

local function adjustSpears()
    local items = getScriptManager():getAllItems()
    local total = 0
    for index = 0, items:size() - 1 do
        local item = items:get(index)
        local name = item:getFullName()
        if originals[name] == nil and isSpear(item) then
            local base = item:getConditionLowerChance()
            if base and base > 0 then
                originals[name] = base
                item:setConditionLowerChance(base * MULTIPLIER)
                total = total + 1
            end
        end
    end
    print("[SpearDurability] spears adjusted: " .. total)
end

Events.OnGameBoot.Add(adjustSpears)
