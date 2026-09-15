<#
.SYNOPSIS
    Локальный токен-прокси: подставляет SSO access_token в запросы к MCP-серверу.

.DESCRIPTION
    Слушает локальный порт, проксирует запросы на upstream (MCP-сервер) и добавляет
    заголовок Authorization: Bearer <token>. Токен берётся по OAuth2 password grant
    и обновляется по refresh_token. Нужен, потому что MCP-клиенты (curl, IDE) не
    умеют логиниться в SSO.

    Переменные окружения:
        MCP_SSO_AUTHORITY  — base URL OAuth-провайдера SSO
        MCP_SSO_CLIENT_ID  — client_id
        MCP_SSO_USERNAME   — AD-логин
        MCP_SSO_PASSWORD   — пароль в виде SecureString-строки
        MCP_SSO_SCOPE      — scope (по умолчанию openid)
        MCP_UPSTREAM        — адрес MCP-сервера (напр. http://127.0.0.1:8085)
        MCP_LISTEN/MCP_PORT — куда слушать (по умолчанию 127.0.0.1:8081)

    Запуск:
        powershell -File scripts/mcp_token_proxy.ps1
#>

param(
    [string]$Listen = $env:MCP_LISTEN,
    [int]$Port = 0
)

$SCRIPT:Authority = $env:MCP_SSO_AUTHORITY
$SCRIPT:ClientId  = $env:MCP_SSO_CLIENT_ID
$SCRIPT:Username  = $env:MCP_SSO_USERNAME
$SCRIPT:Password  = $null
if ($env:MCP_SSO_PASSWORD) {
    $secure = ConvertTo-SecureString $env:MCP_SSO_PASSWORD
    $SCRIPT:Password = [System.Net.NetworkCredential]::new("", $secure).Password
}
$SCRIPT:Scope     = if ($env:MCP_SSO_SCOPE) { $env:MCP_SSO_SCOPE } else { "openid" }
$SCRIPT:Upstream  = $env:MCP_UPSTREAM
$SCRIPT:Listen    = if ($Listen) { $Listen } else { "127.0.0.1" }
$SCRIPT:Port      = if ($Port -gt 0) { $Port } elseif ($env:MCP_PORT) { [int]$env:MCP_PORT } else { 8081 }

if (-not $SCRIPT:Authority -or -not $SCRIPT:ClientId -or -not $SCRIPT:Username) {
    throw "Не заданы MCP_SSO_AUTHORITY / MCP_SSO_CLIENT_ID / MCP_SSO_USERNAME."
}
if (-not $SCRIPT:Upstream) {
    throw "Не задан MCP_UPSTREAM (куда проксировать, напр. http://127.0.0.1:8085)."
}

# Токен и потокобезопасный лок.
$script:Token = @{ Access = $null; Refresh = $null; Exp = 0 }
$script:TokenLock = [object]::new()

function Get-TokenEndpoint {
    return "$SCRIPT:Authority/protocol/openid-connect/token"
}

function Invoke-TokenGrant([hashtable]$Form) {
    $target  = Get-TokenEndpoint
    $body    = ($Form.GetEnumerator() |
        ForEach-Object { [uri]::EscapeDataString($_.Key) + "=" + [uri]::EscapeDataString([string]$_.Value) }) -join "&"

    $response = Invoke-WebRequest -Uri $target -Method Post -Body $body `
        -ContentType "application/x-www-form-urlencoded" -UseBasicParsing
    return ($response.Content | ConvertFrom-Json)
}

function Get-AccessToken {
    # Возвращает текущий access_token, обновляя его при необходимости.
    [System.Threading.Monitor]::Enter($script:TokenLock)
    try {
        $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        if ($script:Token.Access -and $script:Token.Exp -gt ($now + 30)) {
            return $script:Token.Access
        }

        $data = $null
        for ($attempt = 0; $attempt -lt 3; $attempt++) {
            try {
                if ($script:Token.Refresh) {
                    $data = Invoke-TokenGrant @{
                        grant_type    = "refresh_token"
                        client_id     = $SCRIPT:ClientId
                        refresh_token = $script:Token.Refresh
                    }
                }
                else {
                    $data = Invoke-TokenGrant @{
                        grant_type = "password"
                        client_id  = $SCRIPT:ClientId
                        username   = $SCRIPT:Username
                        password   = $SCRIPT:Password
                        scope      = $SCRIPT:Scope
                    }
                }

                break
            }
            catch {
                if ($attempt -eq 2) { throw }
                Start-Sleep -Seconds 1
            }
        }

        $script:Token.Access  = $data.access_token
        # refresh_token в Keycloak может ротироваться — сохраняем новый.
        $script:Token.Refresh = $data.refresh_token
        $script:Token.Exp     = $now + [double]$data.expires_in
        return $script:Token.Access
    }
    finally {
        [System.Threading.Monitor]::Exit($script:TokenLock)
    }
}

function Handle-Context([System.Net.HttpListenerContext]$Context) {
    # Обрабатывает один HTTP-запрос: проксирует на upstream с инжектированным токеном.
    try {
        $target = $SCRIPT:Upstream + $Context.Request.Url.PathAndQuery
        $token  = Get-AccessToken

        $req = [System.Net.Http.HttpRequestMessage]::new(
            [System.Net.Http.HttpMethod]::new($Context.Request.HttpMethod),
            $target)
        $req.Headers.Authorization =
            [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $token)

        # Пробрасываем входящие заголовки, кроме hop-by-hop и задаваемых нами.
        foreach ($name in $Context.Request.Headers.AllKeys) {
            if ($name -in @("Authorization", "Host", "Content-Length", "Connection", "Expect")) {
                continue
            }
            $null = $req.Headers.TryAddWithoutValidation($name, $Context.Request.Headers[$name])
        }

        # Тело запроса читаем целиком — MCP-сообщения небольшие.
        if ($Context.Request.HasEntityBody) {
            $reader = [System.IO.StreamReader]::new($Context.Request.InputStream, $Context.Request.ContentEncoding)
            try {
                $body = $reader.ReadToEnd()
                $req.Content = [System.Net.Http.StringContent]::new(
                    $body, $Context.Request.ContentEncoding, $Context.Request.ContentType)
            }
            finally {
                $reader.Dispose()
            }
        }

        $handler = [System.Net.Http.HttpClientHandler]::new()
        $client  = [System.Net.Http.HttpClient]::new($handler)
        # SSE-стрим живёт долго — таймаут отключаем.
        $client.Timeout = [System.Threading.Timeout]::InfiniteTimeSpan

        $resp = $client.SendAsync(
            $req, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()

        $Context.Response.StatusCode = [int]$resp.StatusCode
        foreach ($h in $resp.Headers) {
            try { $Context.Response.Headers[$h.Key] = ($h.Value -join ",") } catch {}
        }
        foreach ($h in $resp.Content.Headers) {
            try { $Context.Response.Headers[$h.Key] = ($h.Value -join ",") } catch {}
        }
        $Context.Response.Headers.Remove("Content-Length")
        $Context.Response.Headers.Remove("Transfer-Encoding")
        $Context.Response.SendChunked = $true

        $out = $Context.Response.OutputStream
        $in  = $resp.Content.ReadAsStream()
        try {
            $buffer = [byte[]]::new(8192)
            while ($true) {
                $read = $in.Read($buffer, 0, $buffer.Length)
                if ($read -le 0) { break }
                $out.Write($buffer, 0, $read)
                $out.Flush()
            }
        }
        finally {
            $in.Dispose()
        }
    }
    finally {
        try { $Context.Response.Close() } catch {}
    }
}

# — Сервер —
$script:Listener = [System.Net.HttpListener]::new()
$script:Listener.Prefixes.Add("http://$SCRIPT:Listen`:$SCRIPT:Port/")
$script:Listener.Start()

Write-Host "MCP token proxy: http://$SCRIPT:Listen`:$SCRIPT:Port -> $SCRIPT:Upstream"
Write-Host "Получаю первый токен..."
$null = Get-AccessToken
Write-Host "Готово. Нажмите Ctrl+C для остановки."

try {
    while ($script:Listener.IsListening) {
        $context = $script:Listener.GetContext()
        # На каждый запрос — отдельный фоновый поток, чтобы SSE-стрим
        # не блокировал обработку последующих запросов (POST /mcp/message).
        $thread = [System.Threading.Thread]::new(
            [System.Threading.ParameterizedThreadStart]{
                param($ctxObj)
                try { Handle-Context $ctxObj } catch {}
            })
        $thread.IsBackground = $true
        $thread.Start($context)
    }
}
finally {
    $script:Listener.Stop()
}
