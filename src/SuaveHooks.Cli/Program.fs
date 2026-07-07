open System
open System.Collections.Generic
open System.Net.Http
open System.Net.Http.Headers
open System.Net.WebSockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks

module Program =

    type LiveEvent = {
        requestId : string
        method : string
        receivedAt : string
        sourceIp : string
    }

    let private parseArgs (argv : string array) =
        let dict = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        let mutable i = 0
        while i < argv.Length do
            let arg = argv.[i]
            if arg.StartsWith("--") then
                let key = arg
                if i + 1 < argv.Length && not (argv.[i + 1].StartsWith("--")) then
                    dict.[key] <- argv.[i + 1]
                    i <- i + 2
                else
                    dict.[key] <- "true"
                    i <- i + 1
            else
                i <- i + 1
        dict

    let private usage () =
        printfn """suavehooks — local development tools for SuaveHooks

Usage:
  suavehooks listen --server URL --endpoint ID --local URL --api-key KEY
  suavehooks send --url CAPTURE_URL --preset ID [--api-key KEY]

Commands:
  listen   Forward new captures from an endpoint to a local URL (WebSocket live tail)
  send     Send a sample webhook payload to a capture URL

Examples:
  suavehooks listen --server http://localhost:8080 --endpoint abc123... --local http://localhost:3000/webhooks --api-key sh_...
  suavehooks send --url http://localhost:8080/u/USER_ID/my-endpoint --preset stripe-checkout
"""

    let private http = new HttpClient()

    let private fetchRequestDetail (server : string) (apiKey : string) (requestId : string) = task {
        use req = new HttpRequestMessage(HttpMethod.Get, sprintf "%s/api/v1/requests/%s" (server.TrimEnd('/')) requestId)
        req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", apiKey)
        let! resp = http.SendAsync(req)
        let! body = resp.Content.ReadAsStringAsync()
        if not resp.IsSuccessStatusCode then
            return Error (sprintf "API %d: %s" (int resp.StatusCode) body)
        else
            return Ok body
    }

    let private forwardToLocal (localUrl : string) (detailJson : string) = task {
        use doc = JsonDocument.Parse(detailJson)
        let root = doc.RootElement
        let method = root.GetProperty("method").GetString()
        let body = root.GetProperty("body").GetString()
        let bodyBytes = if String.IsNullOrEmpty body then Array.empty else Encoding.UTF8.GetBytes body
        use req = new HttpRequestMessage(new HttpMethod(method), localUrl)
        if bodyBytes.Length > 0 then
            req.Content <- new ByteArrayContent(bodyBytes)
            req.Content.Headers.ContentType <- MediaTypeHeaderValue.Parse("application/json")
        if root.TryGetProperty("headers") |> fst then
            for header in root.GetProperty("headers").EnumerateArray() do
                let name = header.GetProperty("name").GetString()
                let value = header.GetProperty("value").GetString()
                if not (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) then
                    req.Headers.TryAddWithoutValidation(name, value) |> ignore
        let! resp = http.SendAsync(req)
        return int resp.StatusCode
    }

    let private listen (args : IDictionary<string, string>) = task {
        let server = args.["--server"]
        let endpointId = args.["--endpoint"]
        let localUrl = args.["--local"]
        let apiKey = args.["--api-key"]
        let wsUrl =
            let baseUrl = server.TrimEnd('/').Replace("https://", "wss://").Replace("http://", "ws://")
            sprintf "%s/ws/endpoint/%s" baseUrl endpointId
        use ws = new ClientWebSocket()
        ws.Options.SetRequestHeader("Authorization", sprintf "Bearer %s" apiKey)
        printfn "Connecting to %s" wsUrl
        printfn "Forwarding captures to %s" localUrl
        do! ws.ConnectAsync(Uri(wsUrl), CancellationToken.None)
        printfn "Connected. Waiting for webhooks…"
        let buffer = Array.zeroCreate<byte> 65536
        while ws.State = WebSocketState.Open do
            let! result = ws.ReceiveAsync(Memory<byte>(buffer), CancellationToken.None)
            if result.MessageType = WebSocketMessageType.Close then
                do! ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
            else
                let json = Encoding.UTF8.GetString(buffer, 0, result.Count)
                try
                    let evt = JsonSerializer.Deserialize<LiveEvent>(json)
                    printfn "[%s] %s from %s → forwarding %s" evt.receivedAt evt.method evt.sourceIp evt.requestId
                    match! fetchRequestDetail server apiKey evt.requestId with
                    | Error err -> eprintfn "Fetch failed: %s" err
                    | Ok detail ->
                        let! status = forwardToLocal localUrl detail
                        printfn "  → local responded %d" status
                with ex ->
                    eprintfn "Invalid live event: %s (%s)" json ex.Message
        return 0
    }

    let private sampleBodies =
        Map.ofList [
            "generic", """{"event":"test.webhook","message":"Hello from suavehooks CLI"}"""
            "stripe-checkout", """{"id":"evt_test","type":"checkout.session.completed","data":{"object":{"id":"cs_test"}}}"""
            "github-push", """{"ref":"refs/heads/main","repository":{"full_name":"demo/repo"}}"""
        ]

    let private sendSample (args : IDictionary<string, string>) = task {
        let url = args.["--url"]
        let preset = args.TryGetValue("--preset") |> function true, v -> v | _ -> "generic"
        let body = sampleBodies |> Map.tryFind preset |> Option.defaultValue (Map.find "generic" sampleBodies)
        use req = new HttpRequestMessage(HttpMethod.Post, url)
        req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
        req.Headers.TryAddWithoutValidation("User-Agent", "SuaveHooks-CLI/0.1") |> ignore
        req.Headers.TryAddWithoutValidation("X-SuaveHooks-Test", "true") |> ignore
        let! resp = http.SendAsync(req)
        let! text = resp.Content.ReadAsStringAsync()
        printfn "%d %s" (int resp.StatusCode) text
        return if resp.IsSuccessStatusCode then 0 else 1
    }

    [<EntryPoint>]
    let main argv =
        if argv.Length = 0 || argv.[0] = "--help" || argv.[0] = "-h" then
            usage ()
            0
        else
            let command = argv.[0]
            let args = parseArgs (argv |> Array.skip 1)
            try
                match command with
                | "listen" ->
                    for key in [| "--server"; "--endpoint"; "--local"; "--api-key" |] do
                        if not (args.ContainsKey key) then failwithf "Missing %s" key
                    listen args |> Async.AwaitTask |> Async.RunSynchronously
                | "send" ->
                    if not (args.ContainsKey "--url") then failwith "Missing --url"
                    sendSample args |> Async.AwaitTask |> Async.RunSynchronously
                | _ ->
                    eprintfn "Unknown command: %s" command
                    usage ()
                    1
            with ex ->
                eprintfn "Error: %s" ex.Message
                1
