local bridgeUrl
local bridgeToken
local pollInFlight = false
local bridgeWasReachable = false
local lastReportedError

local function log(message)
    print(('[nexus_admin] %s'):format(message))
end

local function postResult(command, success, message, players)
    local result = {
        commandId = command.commandId,
        action = command.action,
        success = success,
        message = message
    }

    if players then
        result.players = players
    end

    PerformHttpRequest(
        bridgeUrl .. '/bridge/results',
        function(statusCode, _, _, errorData)
            if statusCode ~= 200 then
                log(('failed to return action result (HTTP %s): %s'):format(
                    tostring(statusCode),
                    tostring(errorData or 'no response')
                ))
            end
        end,
        'POST',
        json.encode(result),
        {
            ['Content-Type'] = 'application/json',
            ['Authorization'] = 'Bearer ' .. bridgeToken
        }
    )
end

local function hasPermission(permission)
    local resourcePrincipal = 'resource.' .. GetCurrentResourceName()
    return IsPrincipalAceAllowed(resourcePrincipal, permission)
end

local function normalizeMessage(rawMessage)
    if type(rawMessage) ~= 'string' then
        return nil
    end

    local message = rawMessage:gsub('[%c]', ' '):gsub('^%s+', ''):gsub('%s+$', '')
    local messageLength = utf8.len(message)
    if not messageLength or messageLength == 0 or messageLength > Config.MaxNoticeLength then
        return nil
    end

    return message
end

local function listPlayers(command)
    if not hasPermission(Config.AcePermissions.ViewPlayers) then
        log(('denied getPlayers: missing ACE %s'):format(Config.AcePermissions.ViewPlayers))
        postResult(command, false, 'O resource não possui a permissão ACE para consultar jogadores.')
        return
    end

    local players = {}
    for _, rawPlayerId in ipairs(GetPlayers()) do
        local playerId = tonumber(rawPlayerId)
        if playerId then
            players[#players + 1] = {
                id = playerId,
                name = GetPlayerName(rawPlayerId) or 'Desconhecido',
                ping = GetPlayerPing(rawPlayerId) or 0
            }
        end
    end

    log(('player list returned (%d online)'):format(#players))
    postResult(command, true, ('Lista atualizada: %d jogadores online.'):format(#players), players)
end

local function sendNotice(command)
    if not hasPermission(Config.AcePermissions.SendNotice) then
        log(('denied sendNotice: missing ACE %s'):format(Config.AcePermissions.SendNotice))
        postResult(command, false, 'O resource não possui a permissão ACE para enviar avisos.')
        return
    end

    local targetId
    if type(command.targetId) == 'number' or type(command.targetId) == 'string' then
        targetId = tonumber(command.targetId)
    end

    if not targetId or targetId < 1 or targetId % 1 ~= 0 then
        postResult(command, false, 'Selecione um ID de jogador válido.')
        return
    end

    local targetName = GetPlayerName(targetId)
    if not targetName then
        postResult(command, false, 'O jogador selecionado não está mais online.')
        return
    end

    local message = normalizeMessage(command.message)
    if not message then
        postResult(command, false, ('O aviso deve ter entre 1 e %d caracteres.'):format(Config.MaxNoticeLength))
        return
    end

    TriggerClientEvent('nexus_admin:client:notice', targetId, 'Administração', message)
    log(('notice sent to %s (%d)'):format(targetName, targetId))
    postResult(command, true, ('Aviso enviado para %s (%d).'):format(targetName, targetId))
end

local function sendBroadcast(command)
    if not hasPermission(Config.AcePermissions.SendBroadcast) then
        log(('denied sendBroadcast: missing ACE %s'):format(Config.AcePermissions.SendBroadcast))
        postResult(command, false, 'O resource não possui a permissão ACE para avisos globais.')
        return
    end

    local message = normalizeMessage(command.message)
    if not message then
        postResult(command, false, ('O aviso deve ter entre 1 e %d caracteres.'):format(Config.MaxNoticeLength))
        return
    end

    TriggerClientEvent('nexus_admin:client:notice', -1, 'Administração', message)
    log('broadcast notice sent')
    postResult(command, true, 'Aviso global enviado.')
end

local function kickPlayer(command)
    if not hasPermission(Config.AcePermissions.KickPlayer) then
        log(('denied kickPlayer: missing ACE %s'):format(Config.AcePermissions.KickPlayer))
        postResult(command, false, 'O resource não possui a permissão ACE para expulsar jogadores.')
        return
    end

    local targetId
    if type(command.targetId) == 'number' or type(command.targetId) == 'string' then
        targetId = tonumber(command.targetId)
    end

    if not targetId or targetId < 1 or targetId % 1 ~= 0 then
        postResult(command, false, 'Selecione um ID de jogador válido.')
        return
    end

    local targetName = GetPlayerName(targetId)
    if not targetName then
        postResult(command, false, 'O jogador selecionado não está mais online.')
        return
    end

    local reason = normalizeMessage(command.message)
    if not reason then
        postResult(command, false, ('O motivo deve ter entre 1 e %d caracteres.'):format(Config.MaxNoticeLength))
        return
    end

    DropPlayer(targetId, reason)
    log(('player kicked: %s (%d)'):format(targetName, targetId))
    postResult(command, true, ('%s (%d) foi expulso.'):format(targetName, targetId))
end

local function handleCommand(command)
    if type(command) ~= 'table'
        or type(command.commandId) ~= 'string'
        or #command.commandId == 0
        or #command.commandId > 64
        or type(command.expiresAtUnixSeconds) ~= 'number'
        or command.expiresAtUnixSeconds < os.time()
    then
        log('ignored malformed or expired bridge command')
        return
    end

    if command.action == 'getPlayers' then
        listPlayers(command)
    elseif command.action == 'sendNotice' then
        sendNotice(command)
    elseif command.action == 'sendBroadcast' then
        sendBroadcast(command)
    elseif command.action == 'kickPlayer' then
        kickPlayer(command)
    else
        postResult(command, false, 'Ação não autorizada pelo resource.')
    end
end

local function pollBridge()
    if pollInFlight then
        return
    end

    pollInFlight = true
    PerformHttpRequest(
        bridgeUrl .. '/bridge/poll',
        function(statusCode, body, _, errorData)
            pollInFlight = false

            if statusCode == 204 then
                bridgeWasReachable = true
                lastReportedError = nil
                return
            end

            if statusCode == 200 then
                bridgeWasReachable = true
                lastReportedError = nil

                local ok, command = pcall(json.decode, body or '')
                if not ok or type(command) ~= 'table' then
                    log('bridge returned invalid JSON')
                    return
                end

                handleCommand(command)
                return
            end

            local reason = ('HTTP %s: %s'):format(
                tostring(statusCode),
                tostring(errorData or 'bridge unavailable')
            )
            if bridgeWasReachable or reason ~= lastReportedError then
                log('local bridge unavailable (' .. reason .. ')')
            end
            bridgeWasReachable = false
            lastReportedError = reason
        end,
        'POST',
        '{}',
        {
            ['Content-Type'] = 'application/json',
            ['Authorization'] = 'Bearer ' .. bridgeToken
        }
    )
end

CreateThread(function()
    local configuredUrl = GetConvar(Config.BridgeUrlConvar, Config.DefaultBridgeUrl)
    if configuredUrl ~= Config.DefaultBridgeUrl then
        log(('invalid local API URL in convar %s; expected %s'):format(Config.BridgeUrlConvar, Config.DefaultBridgeUrl))
        return
    end

    bridgeToken = GetConvar(Config.BridgeTokenConvar, '')
    if #bridgeToken < 32 or #bridgeToken > 128 or bridgeToken:find('[^%w_-]') then
        log(('configure a 32-128 character ASCII token in convar %s'):format(Config.BridgeTokenConvar))
        return
    end

    bridgeUrl = configuredUrl
    log(('polling local desktop bridge at %s'):format(bridgeUrl))

    while true do
        pollBridge()
        Wait(Config.PollIntervalMs)
    end
end)
