local bridgeUrl, bridgeToken
local pollInFlight = false
local frozenPlayers = {}

local function log(message)
    print(('[nexus_admin] %s'):format(message))
end

local function hasPermission(permission)
    return IsPrincipalAceAllowed('resource.' .. GetCurrentResourceName(), permission)
end

local function postResult(command, success, message, players)
    local result = {
        commandId = command.commandId,
        action = command.action,
        success = success,
        message = message,
        players = players
    }
    PerformHttpRequest(bridgeUrl .. '/bridge/results', function(status)
        if status ~= 200 then log(('falha ao retornar resultado HTTP %s'):format(status)) end
    end, 'POST', json.encode(result), {
        ['Content-Type'] = 'application/json',
        ['Authorization'] = 'Bearer ' .. bridgeToken
    })
end

local function cleanMessage(value)
    if type(value) ~= 'string' then return nil end
    local message = value:gsub('[%c]', ' '):gsub('^%s+', ''):gsub('%s+$', '')
    local length = utf8.len(message)
    if not length or length < 1 or length > Config.MaxNoticeLength then return nil end
    return message
end

local function validTarget(command)
    local id = tonumber(command.targetId)
    if not id or id < 1 or id % 1 ~= 0 or not GetPlayerName(id) then return nil end
    return id
end

local function handleCommand(command)
    if type(command) ~= 'table' or type(command.commandId) ~= 'string'
        or #command.commandId < 1 or #command.commandId > 64
        or type(command.expiresAtUnixSeconds) ~= 'number'
        or command.expiresAtUnixSeconds < os.time() then
        log('comando inválido ou expirado ignorado')
        return
    end

    local action = command.action
    if action == 'getPlayers' then
        if not hasPermission(Config.AcePermissions.ViewPlayers) then
            return postResult(command, false, 'Permissão ACE ausente: ' .. Config.AcePermissions.ViewPlayers)
        end
        local list = {}
        for _, rawId in ipairs(GetPlayers()) do
            local id = tonumber(rawId)
            list[#list + 1] = { id = id, name = GetPlayerName(id) or 'Desconhecido', ping = GetPlayerPing(id) or 0 }
        end
        return postResult(command, true, ('%d jogador(es) online.'):format(#list), list)
    end

    if action == 'sendBroadcast' then
        if not hasPermission(Config.AcePermissions.SendBroadcast) then
            return postResult(command, false, 'Permissão ACE ausente: ' .. Config.AcePermissions.SendBroadcast)
        end
        local message = cleanMessage(command.message)
        if not message then return postResult(command, false, 'Mensagem inválida ou muito longa.') end
        TriggerClientEvent('nexus_admin:client:notice', -1, 'Administração', message)
        return postResult(command, true, 'Aviso global enviado.')
    end

    if action == 'sendNotice' or action == 'kickPlayer' then
        local permission = action == 'sendNotice' and Config.AcePermissions.SendNotice or Config.AcePermissions.KickPlayer
        if not hasPermission(permission) then return postResult(command, false, 'Permissão ACE ausente: ' .. permission) end
        local id = validTarget(command)
        if not id then return postResult(command, false, 'Jogador não encontrado ou ID inválido.') end
        local message = cleanMessage(command.message)
        if not message then return postResult(command, false, 'Mensagem/motivo inválido ou muito longo.') end
        if action == 'sendNotice' then
            TriggerClientEvent('nexus_admin:client:notice', id, 'Administração', message)
            return postResult(command, true, ('Aviso enviado para %s (%d).'):format(GetPlayerName(id), id))
        end
        DropPlayer(id, message)
        return postResult(command, true, ('%s (%d) foi expulso.'):format(GetPlayerName(id), id))
    end

    local actionMap = {
        healPlayer = 'heal',
        revivePlayer = 'revive',
        toggleFreeze = 'freeze'
    }
    local clientAction = actionMap[action]
    if clientAction then
        if not hasPermission(Config.AcePermissions.PlayerActions) then
            return postResult(command, false, 'Permissão ACE ausente: ' .. Config.AcePermissions.PlayerActions)
        end
        local id = validTarget(command)
        if not id then return postResult(command, false, 'Jogador não encontrado ou ID inválido.') end
        if clientAction == 'freeze' then
            frozenPlayers[id] = not frozenPlayers[id]
            TriggerClientEvent('nexus_admin:client:freeze', id, frozenPlayers[id])
            return postResult(command, true, frozenPlayers[id] and 'Jogador congelado.' or 'Jogador liberado.')
        end
        TriggerClientEvent('nexus_admin:client:' .. clientAction, id)
        return postResult(command, true, clientAction == 'heal' and 'Jogador curado.' or 'Jogador revivido.')
    end

    postResult(command, false, 'Ação não reconhecida ou não autorizada.')
end

local function poll()
    if pollInFlight then return end
    pollInFlight = true
    PerformHttpRequest(bridgeUrl .. '/bridge/poll', function(status, body)
        pollInFlight = false
        if status == 200 and body and body ~= '' then
            local ok, command = pcall(json.decode, body)
            if ok and type(command) == 'table' then handleCommand(command) end
        elseif status ~= 204 then
            log(('ponte local indisponível: HTTP %s'):format(tostring(status)))
        end
    end, 'POST', '{}', {
        ['Content-Type'] = 'application/json',
        ['Authorization'] = 'Bearer ' .. bridgeToken
    })
end

CreateThread(function()
    local url = GetConvar(Config.BridgeUrlConvar, Config.DefaultBridgeUrl)
    if url ~= Config.DefaultBridgeUrl then
        log('por segurança, a URL deve permanecer em 127.0.0.1:38765')
        return
    end
    bridgeToken = GetConvar(Config.BridgeTokenConvar, '')
    if #bridgeToken < 32 or #bridgeToken > 128 or bridgeToken:find('[^%w_-]') then
        log('configure um token ASCII de 32 a 128 caracteres no server.cfg')
        return
    end
    bridgeUrl = url
    log('ponte autenticada iniciada.')
    while true do
        poll()
        Wait(Config.PollIntervalMs)
    end
end)

AddEventHandler('playerDropped', function()
    frozenPlayers[source] = nil
end)
