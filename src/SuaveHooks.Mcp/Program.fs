namespace SuaveHooks.Mcp

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks

module ApiClient =

    let private http = new HttpClient()

    let private baseUrl () =
        (Environment.GetEnvironmentVariable "SUAVEHOOKS_API_URL"
         |> Option.ofObj
         |> Option.defaultValue "http://localhost:8080").TrimEnd('/')

    let apiKey () =
        Environment.GetEnvironmentVariable "SUAVEHOOKS_API_KEY"
        |> Option.ofObj
        |> Option.defaultValue ""

    let private authed (method : HttpMethod) (path : string) (body : string option) = task {
        use req = new HttpRequestMessage(method, baseUrl() + path)
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey()) |> ignore
        match body with
        | Some json ->
            req.Content <- new StringContent(json, Encoding.UTF8, "application/json")
        | None -> ()
        use! resp = http.SendAsync(req)
        let! text = resp.Content.ReadAsStringAsync()
        return int resp.StatusCode, text
    }

    let listEndpoints () = authed HttpMethod.Get "/api/v1/endpoints" None

    let createEndpoint (name : string) (targetUrl : string option) = task {
        let target =
            match targetUrl with
            | Some url -> sprintf ",\"targetUrl\":\"%s\"" (url.Replace("\"", "\\\""))
            | None -> ""
        let json = sprintf """{"name":"%s"%s}""" (name.Replace("\"", "\\\"")) target
        return! authed HttpMethod.Post "/api/v1/endpoints" (Some json)
    }

    let listRequests (endpointId : string) (limit : int) =
        authed HttpMethod.Get (sprintf "/api/v1/endpoints/%s/requests?limit=%d" endpointId limit) None

    let getRequest (requestId : string) =
        authed HttpMethod.Get (sprintf "/api/v1/requests/%s" requestId) None

    let replayRequest (requestId : string) (targetUrl : string option) = task {
        let json =
            match targetUrl with
            | Some url -> sprintf """{"targetUrl":"%s"}""" (url.Replace("\"", "\\\""))
            | None -> "{}"
        return! authed HttpMethod.Post (sprintf "/api/v1/requests/%s/replay" requestId) (Some json)
    }

module McpServer =

    type JsonRpcRequest = {
        jsonrpc : string
        id : JsonElement
        method : string
        params : JsonElement
    }

    let private writeResponse (id : JsonElement) (result : obj) =
        let payload = {| jsonrpc = "2.0"; id = id; result = result |}
        let json = JsonSerializer.Serialize(payload)
        Console.Out.WriteLine(json)
        Console.Out.Flush()

    let private writeError (id : JsonElement) (code : int) (message : string) =
        let payload =
            {| jsonrpc = "2.0"
               id = id
               error = {| code = code; message = message |} |}
        let json = JsonSerializer.Serialize(payload)
        Console.Out.WriteLine(json)
        Console.Out.Flush()

    let private tools =
        [|
            {| name = "list_endpoints"; description = "List webhook endpoints for the authenticated user"; inputSchema = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""") |}
            {| name = "create_endpoint"; description = "Create a new capture endpoint"; inputSchema = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{"name":{"type":"string"},"targetUrl":{"type":"string"}},"required":["name"]}""") |}
            {| name = "list_requests"; description = "List captured requests for an endpoint"; inputSchema = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{"endpointId":{"type":"string"},"limit":{"type":"integer"}},"required":["endpointId"]}""") |}
            {| name = "get_request"; description = "Get full capture details including body and forward deliveries"; inputSchema = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{"requestId":{"type":"string"}},"required":["requestId"]}""") |}
            {| name = "replay_request"; description = "Replay a captured request to a target URL"; inputSchema = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{"requestId":{"type":"string"},"targetUrl":{"type":"string"}},"required":["requestId"]}""") |}
        |]

    let private readArg (args : JsonElement) (name : string) =
        match args.TryGetProperty(name) with
        | true, p when p.ValueKind = JsonValueKind.String -> Some (p.GetString())
        | _ -> None

    let private readIntArg (args : JsonElement) (name : string) (defaultValue : int) =
        match args.TryGetProperty(name) with
        | true, p when p.ValueKind = JsonValueKind.Number ->
            match p.TryGetInt32() with
            | true, v -> v
            | _ -> defaultValue
        | _ -> defaultValue

    let private callTool (name : string) (args : JsonElement) = task {
        if String.IsNullOrWhiteSpace(ApiClient.apiKey()) then
            return Error "SUAVEHOOKS_API_KEY is not set"
        else
            match name with
            | "list_endpoints" ->
                let! code, body = ApiClient.listEndpoints()
                if code >= 200 && code < 300 then return Ok [| {| ``type`` = "text"; text = body |} |]
                else return Error (sprintf "API %d: %s" code body)
            | "create_endpoint" ->
                match readArg args "name" with
                | None -> return Error "name is required"
                | Some n ->
                    let! code, body = ApiClient.createEndpoint n (readArg args "targetUrl")
                    if code >= 200 && code < 300 then return Ok [| {| ``type`` = "text"; text = body |} |]
                    else return Error (sprintf "API %d: %s" code body)
            | "list_requests" ->
                match readArg args "endpointId" with
                | None -> return Error "endpointId is required"
                | Some epId ->
                    let limit = readIntArg args "limit" 20
                    let! code, body = ApiClient.listRequests epId limit
                    if code >= 200 && code < 300 then return Ok [| {| ``type`` = "text"; text = body |} |]
                    else return Error (sprintf "API %d: %s" code body)
            | "get_request" ->
                match readArg args "requestId" with
                | None -> return Error "requestId is required"
                | Some reqId ->
                    let! code, body = ApiClient.getRequest reqId
                    if code >= 200 && code < 300 then return Ok [| {| ``type`` = "text"; text = body |} |]
                    else return Error (sprintf "API %d: %s" code body)
            | "replay_request" ->
                match readArg args "requestId" with
                | None -> return Error "requestId is required"
                | Some reqId ->
                    let! code, body = ApiClient.replayRequest reqId (readArg args "targetUrl")
                    if code >= 200 && code < 300 then return Ok [| {| ``type`` = "text"; text = body |} |]
                    else return Error (sprintf "API %d: %s" code body)
            | _ -> return Error (sprintf "Unknown tool: %s" name)
    }

    let private handle (req : JsonRpcRequest) = task {
        match req.method with
        | "initialize" ->
            writeResponse req.id {|
                protocolVersion = "2024-11-05"
                capabilities = {| tools = {| listChanged = false |} |}
                serverInfo = {| name = "suavehooks"; version = "1.0.0" |}
            |}
        | "tools/list" ->
            writeResponse req.id {| tools = tools |}
        | "tools/call" ->
            let name =
                match req.params.TryGetProperty("name") with
                | true, p -> p.GetString()
                | _ -> ""
            let args =
                match req.params.TryGetProperty("arguments") with
                | true, p -> p
                | _ -> JsonSerializer.Deserialize<JsonElement>("{}")
            let! result = callTool name args
            match result with
            | Ok content -> writeResponse req.id {| content = content; isError = false |}
            | Error msg -> writeResponse req.id {| content = [| {| ``type`` = "text"; text = msg |} |]; isError = true |}
        | "ping" ->
            writeResponse req.id {| |}
        | _ ->
            writeError req.id -32601 (sprintf "Method not found: %s" req.method)
    }

    let run () =
        Console.Error.WriteLine("SuaveHooks MCP server started (stdio)")
        while true do
            let line = Console.In.ReadLine()
            if isNull line then ()
            elif String.IsNullOrWhiteSpace line then ()
            else
                try
                    use doc = JsonDocument.Parse(line)
                    let root = doc.RootElement
                    if not (root.TryGetProperty("method") |> fst) then ()
                    else
                        let method = root.GetProperty("method").GetString()
                        if method = "notifications/initialized" then ()
                        else
                            let req = JsonSerializer.Deserialize<JsonRpcRequest>(line)
                            handle req |> Async.AwaitTask |> Async.RunSynchronously
                with ex ->
                    Console.Error.WriteLine(sprintf "MCP parse error: %s" ex.Message)

module Program =

    [<EntryPoint>]
    let main _argv =
        McpServer.run ()
        0
