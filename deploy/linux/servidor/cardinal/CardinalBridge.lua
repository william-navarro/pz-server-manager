-- CardinalBridge.lua - ponte estritamente server-side para Project Zomboid B42.21.
-- Qualquer erro fica contido em pcall: a Cardinal nunca pode derrubar o servidor.
if not isServer() then return end

local Cardinal = {
    lastSnapshotMs = 0,
    lastActionsMs = 0,
    sleeping = {},
    lastWakeMs = {},
    buildingVisits = {},
    fogUntilMs = nil,
}

-- O jogo só deixa a Lua ler/gravar extensões permitidas (ALLOWED_FILE_EXTENSIONS: .txt/.lua);
-- por isso a troca de arquivos com o painel usa .txt com conteúdo JSON.
local ACTION_DIR = "cardinal/acoes"
local ITEM_ALLOW = {
    ["Base.FirstAidKit"] = true, ["Base.CannedFood"] = true, ["Base.556Box"] = true,
    ["Base.9mmBox"] = true, ["Base.Hammer"] = true,
}
local OUTFIT_ALLOW = {
    -- Nomes conferidos em media/clothing/clothing.xml do B42.21.
    ArmyCamoGreen = true, ArmyCamoDesert = true, Police = true, PoliceRiot = true, Police_SWAT = true,
    Bandit = true, Bandit_Mid = true, Bandit_Late = true, Survivalist = true, Hunter = true,
}
local ALLOWED = {
    horda_direcionada = true, horda_tematica = true, som = true, item_raro = true,
    alife_raid = true, alife_scouts = true, alarme = true, tempestade = true,
    neblina = true, esconderijo = true, airdrop = true, veiculo_abandonado = true,
}

local function logError(where, value)
    print("[Cardinal] " .. tostring(where) .. ": " .. tostring(value))
end

local function clamp(value, minimum, maximum, fallback)
    value = tonumber(value)
    if value == nil then return fallback end
    return math.max(minimum, math.min(maximum, value))
end

local function escape(value)
    return tostring(value):gsub("\\", "\\\\"):gsub('"', '\\"'):gsub("\n", "\\n"):gsub("\r", "\\r"):gsub("\t", "\\t")
end

local function isArray(value)
    if type(value) ~= "table" then return false end
    local maximum, count = 0, 0
    for key, _ in pairs(value) do
        if type(key) ~= "number" or key < 1 or key % 1 ~= 0 then return false end
        maximum, count = math.max(maximum, key), count + 1
    end
    return maximum == count
end

local function json(value)
    local kind = type(value)
    if value == nil then return "null" end
    if kind == "boolean" then return value and "true" or "false" end
    if kind == "number" then
        if value ~= value or value == math.huge or value == -math.huge then return "null" end
        return tostring(value)
    end
    if kind == "string" then return '"' .. escape(value) .. '"' end
    if kind ~= "table" then return '"' .. escape(value) .. '"' end
    local parts = {}
    if isArray(value) then
        for index = 1, #value do parts[#parts + 1] = json(value[index]) end
        return "[" .. table.concat(parts, ",") .. "]"
    end
    for key, item in pairs(value) do parts[#parts + 1] = json(tostring(key)) .. ":" .. json(item) end
    table.sort(parts)
    return "{" .. table.concat(parts, ",") .. "}"
end

local function writeFile(relativePath, text)
    local writer = getFileWriter(relativePath, true, false)
    if writer == nil then error("não foi possível abrir " .. relativePath) end
    writer:write(text)
    writer:close()
end

local function writeAtomic(relativePath, text)
    local directory, base = relativePath:match("^(.*)/([^/]+)$")
    local temporary = directory .. "/_tmp-" .. base
    writeFile(temporary, text)
    local separator = getFileSeparator()
    local root = getMyDocumentFolder() .. separator .. "Lua" .. separator
    local oldPath = root .. temporary:gsub("/", separator)
    local newPath = root .. relativePath:gsub("/", separator)
    local renamed = false
    if type(os) == "table" and type(os.rename) == "function" then
        local ok, result = pcall(os.rename, oldPath, newPath)
        renamed = ok and result ~= nil
    end
    -- Builds que removem os.rename do sandbox ainda são seguras para o leitor: JSON é uma única linha,
    -- e o painel ignora JsonException durante a janela de substituição.
    if not renamed then writeFile(relativePath, text) end
end

local function readFile(relativePath)
    local reader = getFileReader(relativePath, false)
    if reader == nil then return nil end
    local lines = {}
    while true do
        local line = reader:readLine()
        if line == nil then break end
        lines[#lines + 1] = line
    end
    reader:close()
    return table.concat(lines, "\n")
end

local function fileExistsInLua(relativePath)
    local ok, contents = pcall(readFile, relativePath)
    return ok and contents ~= nil
end

local function stringField(text, name)
    local raw = text:match('"' .. name .. '"%s*:%s*"(.-)"')
    if raw == nil then return nil end
    return raw:gsub("\\n", "\n"):gsub("\\r", "\r"):gsub('\\"', '"'):gsub("\\\\", "\\")
end

local function numberField(text, name, fallback)
    -- O painel manda os campos de "parameters" como texto ("x":"10620"); aceita número com ou sem aspas.
    return tonumber(text:match('"' .. name .. '"%s*:%s*"?(-?[%d%.]+)')) or fallback
end

local function booleanField(text, name)
    return text:match('"' .. name .. '"%s*:%s*true') ~= nil
end

local function findPlayer(username)
    if type(username) ~= "string" or #username < 1 or #username > 32 then return nil end
    local players = getOnlinePlayers()
    for index = 0, players:size() - 1 do
        local player = players:get(index)
        if player:getUsername() == username then return player end
    end
    return nil
end

local function moodle(player, moodleType)
    if moodleType == nil then return 0 end
    local ok, value = pcall(function() return player:getMoodles():getMoodleLevel(moodleType) end)
    return ok and tonumber(value) or 0
end

local function bodyState(player)
    local state = { health = 100, infected = false, bleeding = false, fracture = false }
    pcall(function()
        local damage = player:getBodyDamage()
        state.health = damage:getHealth()
        state.infected = damage:IsInfected()
        state.bleeding = damage:getNumPartsBleeding() > 0
        local parts = damage:getBodyParts()
        for index = 0, parts:size() - 1 do
            if parts:get(index):getFractureTime() > 0 then state.fracture = true break end
        end
    end)
    return state
end

local function reading(player)
    local result = false
    pcall(function()
        local actions = player:getCharacterActions()
        for index = 0, actions:size() - 1 do
            if tostring(actions:get(index)):lower():find("read", 1, true) then result = true break end
        end
    end)
    return result
end

local function ammoState(player, weapon)
    local current, total = 0, 0
    if weapon == nil then return current, total end
    local ammoType = nil
    pcall(function() current = weapon:getCurrentAmmoCount() or 0; ammoType = weapon:getAmmoType() end)
    if ammoType == nil then return current, total end
    pcall(function()
        local items = player:getInventory():getAllTypeRecurse(ammoType)
        total = items and items:size() or 0
    end)
    return current, total
end

local function nearbyZombies(player)
    local counts = { z20 = 0, z30 = 0, z50 = 0 }
    local zombies = getCell():getZombieList()
    for index = 0, zombies:size() - 1 do
        local zombie = zombies:get(index)
        local dx, dy = zombie:getX() - player:getX(), zombie:getY() - player:getY()
        local distance = math.sqrt(dx * dx + dy * dy)
        if distance <= 50 then
            counts.z50 = counts.z50 + 1
            if distance <= 30 then counts.z30 = counts.z30 + 1 end
            if distance <= 20 then counts.z20 = counts.z20 + 1 end
        end
    end
    return counts
end

local function currentBuildingKey(player)
    local key = nil
    pcall(function()
        local square = player:getCurrentSquare()
        local building = square and square:getBuilding()
        local definition = building and building:getDef()
        if definition ~= nil then key = tostring(definition:getID()) end
    end)
    return key
end

local function trackBase(player, worldHours)
    local username, key = player:getUsername(), currentBuildingKey(player)
    local history = Cardinal.buildingVisits[username] or {}
    Cardinal.buildingVisits[username] = history
    if key ~= nil then history[#history + 1] = { key = key, hour = worldHours } end
    local counts, retained = {}, {}
    for _, visit in ipairs(history) do
        if worldHours - visit.hour <= 24 then
            retained[#retained + 1] = visit
            counts[visit.key] = (counts[visit.key] or 0) + 1
        end
    end
    Cardinal.buildingVisits[username] = retained
    local best, maximum = nil, 0
    for building, count in pairs(counts) do if count > maximum then best, maximum = building, count end end
    return key ~= nil and key == best and maximum >= 3
end

local function snapshotPlayer(player, worldHours, nowMs)
    local username = player:getUsername()
    local asleep = false
    pcall(function() asleep = player:isAsleep() end)
    if Cardinal.sleeping[username] == true and not asleep then Cardinal.lastWakeMs[username] = nowMs end
    Cardinal.sleeping[username] = asleep
    local body = bodyState(player)
    local okNearby, nearby = pcall(nearbyZombies, player)
    if not okNearby then nearby = { z20 = 0, z30 = 0, z50 = 0 } end
    local weapon = player:getPrimaryHandItem()
    local weaponName, hasMelee = nil, false
    if weapon ~= nil then
        pcall(function() weaponName = weapon:getFullType() end)
        pcall(function() hasMelee = weapon:isTwoHandWeapon() or (weapon:getMaxDamage() or 0) > 0 and weapon:getAmmoType() == nil end)
    end
    local weaponAmmo, inventoryAmmo = ammoState(player, weapon)
    local vehicle, speed = nil, 0
    pcall(function() vehicle = player:getVehicle(); if vehicle ~= nil then speed = vehicle:getCurrentSpeedKmHour() end end)
    local square = player:getCurrentSquare()
    local kills = 0
    pcall(function()
        local modData = player:getModData()
        kills = tonumber(modData.KillCount or modData.killCount) or player:getZombieKills()
    end)
    return {
        username = username, x = player:getX(), y = player:getY(), z = player:getZ(),
        healthPercent = body.health, infected = body.infected, bleeding = body.bleeding, fracture = body.fracture,
        asleep = asleep, sitting = player:isSitOnGround(), reading = reading(player),
        inVehicle = vehicle ~= nil, vehicleSpeed = speed,
        insideBuilding = square ~= nil and square:getBuilding() ~= nil, atBase = trackBase(player, worldHours),
        handWeapon = weaponName, hasMeleeWeapon = hasMelee, weaponAmmo = weaponAmmo, inventoryAmmo = inventoryAmmo,
        carryWeight = player:getInventoryWeight(), maximumCarryWeight = player:getMaxWeight(),
        zombies20 = nearby.z20, zombies30 = nearby.z30, zombies50 = nearby.z50,
        kills = kills, hoursSurvived = player:getHoursSurvived(), groupSize = 1,
        lastWakeAt = Cardinal.lastWakeMs[username] and os.date("!%Y-%m-%dT%H:%M:%SZ", math.floor(Cardinal.lastWakeMs[username] / 1000)) or nil,
        moodles = {
            hunger = moodle(player, MoodleType.HUNGRY), thirst = moodle(player, MoodleType.THIRST),
            pain = moodle(player, MoodleType.PAIN), panic = moodle(player, MoodleType.PANIC),
            tired = moodle(player, MoodleType.TIRED), stress = moodle(player, MoodleType.STRESS),
            drunk = moodle(player, MoodleType.DRUNK), sick = moodle(player, MoodleType.SICK),
            cold = moodle(player, MoodleType.HYPOTHERMIA), hot = moodle(player, MoodleType.HYPERTHERMIA),
        }
    }
end

local function writeSnapshot()
    local nowMs, gameTime = getTimestampMs(), getGameTime()
    local worldHours = gameTime:getWorldAgeHours()
    local players, source = {}, getOnlinePlayers()
    for index = 0, source:size() - 1 do
        -- Um método ausente nesta build (ou um jogador em transição) não pode apagar o snapshot dos outros.
        local ok, entry = pcall(snapshotPlayer, source:get(index), worldHours, nowMs)
        if ok then players[#players + 1] = entry else logError("snapshot do jogador", entry) end
    end
    for _, player in ipairs(players) do
        local group = 1
        for _, other in ipairs(players) do
            if other ~= player then
                local dx, dy = other.x - player.x, other.y - player.y
                if math.sqrt(dx * dx + dy * dy) <= 30 then group = group + 1 end
            end
        end
        player.groupSize = group
    end
    local climate = getClimateManager()
    local hour = gameTime:getHour()
    local state = {
        timestamp = os.date("!%Y-%m-%dT%H:%M:%SZ", math.floor(nowMs / 1000)),
        worldAgeHours = worldHours, day = gameTime:getDay(), hour = hour, minute = gameTime:getMinutes(),
        season = tostring(climate:getSeasonName()), night = hour < 6 or hour >= 21,
        lightLevel = climate:getDayLightStrength(), rainIntensity = climate:getRainIntensity(),
        fogIntensity = climate:getFogIntensity(), weather = climate:isRaining() and "chuva" or "limpo",
        players = players,
    }
    writeAtomic("cardinal/estado.txt", json(state))
end

local function squareIsSafeSpawn(player, square)
    if square == nil or square:getBuilding() ~= nil or not square:isSolidFloor() or square:hasWater() then return false end
    local checked, line = pcall(function()
        return LosUtil.lineClear(getCell(), math.floor(player:getX()), math.floor(player:getY()), math.floor(player:getZ()),
            square:getX(), square:getY(), square:getZ(), false)
    end)
    -- Só aceita quando a linha de visão está comprovadamente bloqueada; falhar fechado é mais seguro.
    if not checked or tostring(line) ~= "Blocked" then return false end
    return true
end

local function findSide(player, distance, maximumDistance)
    local sides = { { 0, -1, "norte" }, { 1, 0, "leste" }, { 0, 1, "sul" }, { -1, 0, "oeste" } }
    for index = #sides, 2, -1 do local swap = ZombRand(index) + 1; sides[index], sides[swap] = sides[swap], sides[index] end
    for _, side in ipairs(sides) do
        for radius = distance, maximumDistance, 5 do
            local square = getSquare(math.floor(player:getX() + side[1] * radius), math.floor(player:getY() + side[2] * radius), 0)
            if squareIsSafeSpawn(player, square) then return square, side[3] end
        end
    end
    return nil, nil
end

local function attractAt(player, x, y, z, radius)
    getWorldSoundManager():addSound(nil, math.floor(x), math.floor(y), math.floor(z), radius or 80, 100, true)
end

local function spawnHorde(action, themed)
    local player = findPlayer(action.player)
    if player == nil then return false, "jogador não está online" end
    local amount = math.floor(clamp(action.amount, 1, 50, 10))
    local distance = math.floor(clamp(action.distance, 25, 80, 30))
    local square, side = findSide(player, distance, math.min(80, distance + 30))
    if square == nil then return false, "nenhum lado fora da visão com chão válido" end
    local outfit = themed and action.outfit or nil
    if themed and not OUTFIT_ALLOW[outfit] then
        -- Sem roupa escolhida pelo painel: sorteia uma da lista branca.
        local names = {}
        for name, _ in pairs(OUTFIT_ALLOW) do names[#names + 1] = name end
        table.sort(names)
        outfit = names[ZombRand(#names) + 1]
    end
    local zombies = addZombiesInOutfit(square:getX(), square:getY(), square:getZ(), amount, outfit, 50)
    if zombies == nil or zombies:size() == 0 then return false, "o jogo não criou a horda" end
    for index = 0, zombies:size() - 1 do
        local zombie = zombies:get(index)
        pcall(function() zombie:pathToLocation(math.floor(player:getX()), math.floor(player:getY()), math.floor(player:getZ())) end)
    end
    attractAt(player, player:getX(), player:getY(), player:getZ(), 90)
    return true, tostring(zombies:size()) .. " zumbis vieram pelo " .. side
end

local function findBuildingSquare(player, minimum, maximum)
    for attempt = 1, 160 do
        local angle = ZombRandFloat(0, math.pi * 2)
        local distance = ZombRand(minimum, maximum + 1)
        local square = getSquare(math.floor(player:getX() + math.cos(angle) * distance), math.floor(player:getY() + math.sin(angle) * distance), 0)
        if square ~= nil and square:getBuilding() ~= nil then return square, distance, angle end
    end
    return nil, nil, nil
end

local function addKit(container, allowedKits, player)
    local value = tostring(allowedKits or ""):lower()
    local types = {}
    if value == "" or value:find("curativo", 1, true) then types[#types + 1] = "Base.FirstAidKit" end
    if value == "" or value:find("comida", 1, true) then types[#types + 1] = "Base.CannedFood"; types[#types + 1] = "Base.CannedFood" end
    if value == "" or value:find("ferramenta", 1, true) then types[#types + 1] = "Base.Hammer" end
    if value:find("muni", 1, true) then
        local ammoType = nil
        if player ~= nil then pcall(function() local weapon = player:getPrimaryHandItem(); if weapon ~= nil then ammoType = weapon:getAmmoType() end end) end
        if ammoType ~= nil then for _ = 1, 20 do types[#types + 1] = ammoType end
        else types[#types + 1] = "Base.556Box"; types[#types + 1] = "Base.9mmBox" end
    end
    local added = 0
    for _, itemType in ipairs(types) do
        local item = container:AddItem(itemType)
        if item ~= nil then added = added + 1; pcall(sendAddItemToContainer, container, item) end
    end
    return added
end

local function supplyCache(player, action)
    local square, distance, angle = nil, nil, nil
    local fixedX, fixedY = tonumber(action.x), tonumber(action.y)
    if fixedX ~= nil and fixedY ~= nil then
        local radius = math.floor(clamp(action.radius, 1, 500, 10))
        for attempt = 1, 80 do
            local candidate = getSquare(math.floor(fixedX + ZombRand(-radius, radius + 1)), math.floor(fixedY + ZombRand(-radius, radius + 1)), math.floor(tonumber(action.z) or 0))
            if candidate ~= nil and candidate:getBuilding() ~= nil then square = candidate break end
        end
        if square ~= nil then
            local dx, dy = square:getX() - player:getX(), square:getY() - player:getY()
            distance, angle = math.floor(math.sqrt(dx * dx + dy * dy)), math.atan2(dy, dx)
        end
    end
    if square == nil then square, distance, angle = findBuildingSquare(player, 100, 400) end
    if square == nil then return false, "nenhum prédio carregado entre 100 e 400 tiles" end
    local objects = square:getObjects()
    for index = 0, objects:size() - 1 do
        local object = objects:get(index)
        local container = object:getContainer()
        if container ~= nil then
            local added = addKit(container, action.kits, player)
            if added > 0 then
                pcall(sendItemsInContainer, object, container)
                local direction = math.abs(math.cos(angle)) > math.abs(math.sin(angle)) and (math.cos(angle) > 0 and "leste" or "oeste") or (math.sin(angle) > 0 and "sul" or "norte")
                return true, "suprimentos em um prédio a cerca de " .. tostring(distance) .. " tiles ao " .. direction
            end
        end
    end
    return false, "o prédio escolhido não tinha contêiner disponível"
end

local function airdrop(player)
    local square, side = findSide(player, 50, 100)
    if square == nil then return false, "nenhum ponto aberto válido para o airdrop" end
    local kit = { "Base.FirstAidKit", "Base.CannedFood", "Base.556Box" }
    for _, itemType in ipairs(kit) do square:AddWorldInventoryItem(itemType, ZombRandFloat(0.2, 0.8), ZombRandFloat(0.2, 0.8), 0) end
    IsoFireManager.StartSmoke(getCell(), square, true, 100, 500)
    attractAt(player, square:getX(), square:getY(), square:getZ(), 120)
    return true, "airdrop com fumaça ao " .. side
end

local function abandonedVehicle(player)
    local square, side = findSide(player, 60, 120)
    if square == nil then return false, "nenhum ponto válido para o veículo" end
    local vehicle = addVehicleDebug("Base.PickUpTruck", IsoDirections.N, nil, square)
    if vehicle == nil then return false, "o jogo não criou o veículo" end
    pcall(function()
        local tank = vehicle:getPartById("GasTank")
        if tank ~= nil then tank:setContainerContentAmount(20) end
        local trunk = vehicle:getPartById("TruckBed") or vehicle:getPartById("Trunk")
        if trunk ~= nil and trunk:getItemContainer() ~= nil then addKit(trunk:getItemContainer(), nil, nil) end
    end)
    return true, "veículo abandonado ao " .. side
end

local function buildingAlarm(player)
    local square, distance = findBuildingSquare(player, 25, 80)
    if square == nil then return false, "nenhum prédio próximo carregado" end
    local building = square:getBuilding()
    building:getDef():setAlarmed(true)
    pcall(function() building:TriggerAlarm() end)
    attractAt(player, square:getX(), square:getY(), square:getZ(), 120)
    return true, "alarme acionado a cerca de " .. tostring(distance) .. " tiles"
end

local function execute(action)
    if not ALLOWED[action.type] then return false, "tipo fora da lista branca" end
    if action.type == "horda_direcionada" then return spawnHorde(action, false) end
    if action.type == "horda_tematica" then return spawnHorde(action, true) end
    local player = findPlayer(action.player)
    if player == nil then return false, "jogador não está online" end
    if action.type == "som" then attractAt(player, player:getX() + action.distance, player:getY(), player:getZ(), 100); return true, "som atrator criado" end
    if action.type == "item_raro" then
        if not ITEM_ALLOW[action.itemType] then return false, "item fora da lista branca" end
        local item = player:getInventory():AddItem(action.itemType)
        if item == nil then return false, "item inválido" end
        sendAddItemToContainer(player:getInventory(), item)
        return true, "item permitido entregue"
    end
    if action.type == "alife_raid" or action.type == "alife_scouts" then
        if ProjectALife == nil or ProjectALife.RaidDirector == nil then return false, "Project A-Life não está carregado" end
        if action.type == "alife_raid" then return ProjectALife.RaidDirector.start(player, {}, action.force == true) end
        return ProjectALife.RaidDirector.sendScouts(player, {})
    end
    if action.type == "alarme" then return buildingAlarm(player) end
    if action.type == "tempestade" then
        local ok = getClimateManager():triggerCustomWeatherStage(WeatherPeriod.STAGE_STORM, 4)
        return ok == true, ok == true and "tempestade iniciada" or "clima recusou a tempestade"
    end
    if action.type == "neblina" then
        local climate = getClimateManager()
        local fog = climate:getClimateFloat(ClimateManager.FLOAT_FOG_INTENSITY)
        fog:setEnableAdmin(true); fog:setAdminValue(0.85)
        Cardinal.fogUntilMs = getTimestampMs() + 600000
        return true, "neblina administrativa iniciada"
    end
    if action.type == "esconderijo" then return supplyCache(player, action) end
    if action.type == "airdrop" then return airdrop(player) end
    if action.type == "veiculo_abandonado" then return abandonedVehicle(player) end
    return false, "ação sem executor"
end

local function parseAction(contents)
    local action = {
        id = stringField(contents, "id"), type = stringField(contents, "type"), player = stringField(contents, "player"),
        itemType = stringField(contents, "itemType"), message = stringField(contents, "message"),
        amount = numberField(contents, "amount", 0), distance = numberField(contents, "distance", 30),
        outfit = stringField(contents, "outfit"),
        x = numberField(contents, "x", nil), y = numberField(contents, "y", nil), z = numberField(contents, "z", nil),
        radius = numberField(contents, "radius", nil),
        kits = stringField(contents, "kits"),
        force = booleanField(contents, "force"),
    }
    if action.id == nil or not action.id:match("^[A-Za-z0-9_-]+$") or #action.id > 80 then return nil, "id inválido" end
    if action.type == nil or not ALLOWED[action.type] then return nil, "tipo inválido" end
    if action.player ~= nil and (#action.player < 1 or #action.player > 32 or action.player:find('["\r\n]')) then return nil, "jogador inválido" end
    if action.amount < 0 or action.amount > 100 or action.distance < 25 or action.distance > 500 then return nil, "limites inválidos" end
    return action, nil
end

local function writeResult(id, success, message)
    writeAtomic(ACTION_DIR .. "/resultado-" .. id .. ".txt", json({ id = id, success = success == true, message = tostring(message or "") }))
end

local function processActions()
    local files = listFilesInZomboidLuaDirectory(ACTION_DIR)
    for index = 0, files:size() - 1 do
        local name = tostring(files:get(index)):gsub("\\", "/")
        local short = name:match("([^/]+)$") or name
        if short:match("^[A-Za-z0-9-][A-Za-z0-9_-]*%.txt$") and not short:match("^resultado%-") then
            local relative = name:find("cardinal/acoes", 1, true) and name or ACTION_DIR .. "/" .. short
            local expectedId = short:gsub("%.txt$", "")
            if not fileExistsInLua(ACTION_DIR .. "/resultado-" .. expectedId .. ".txt") then
                local ok, contents = pcall(readFile, relative)
                if ok and contents ~= nil then
                    local action, parseError = parseAction(contents)
                    if action == nil then writeResult(expectedId, false, parseError)
                    else
                        local executed, success, message = pcall(execute, action)
                        if not executed then writeResult(action.id, false, success)
                        else writeResult(action.id, success == true, message) end
                    end
                end
            end
        end
    end
end

local function onTick()
    local now = getTimestampMs()
    if Cardinal.fogUntilMs ~= nil and now >= Cardinal.fogUntilMs then
        Cardinal.fogUntilMs = nil
        pcall(function() getClimateManager():getClimateFloat(ClimateManager.FLOAT_FOG_INTENSITY):setEnableAdmin(false) end)
    end
    if now - Cardinal.lastActionsMs >= 10000 then
        Cardinal.lastActionsMs = now
        local ok, value = pcall(processActions)
        if not ok then logError("fila de ações", value) end
    end
    if now - Cardinal.lastSnapshotMs >= 60000 then
        Cardinal.lastSnapshotMs = now
        local ok, value = pcall(writeSnapshot)
        if not ok then logError("snapshot", value) end
    end
end

Events.OnTick.Add(onTick)
print("[Cardinal] ponte B42.21 carregada no servidor")
