module WebSocketHub

open System.Net.WebSockets
open FSharp.Control.Tasks.ContextInsensitive
open System.Text
open System
open System.Threading
open Microsoft.AspNetCore.Http
open System.Threading.Tasks
open Giraffe
open System.Collections.Concurrent
open System.IO

type private LeasedSocket = {
    Id: Guid
    Socket: WebSocket
    Release: Unit -> unit
}

type private SocketMessageReceived =
    | TextReceived of TextMessageReceived
    | CloseReceived of CloseMessageReceived
    | NothingReceived of unit
and TextMessageReceived = {
    Text: string
    Timestamp: DateTime
}
and CloseMessageReceived = {
    CloseStatus: WebSocketCloseStatus
    CloseStatusDescription: string
    Timestamp: DateTime
}

let private sockets = new ConcurrentDictionary<Guid, LeasedSocket>()

let private sendMessage (leasedSocket: LeasedSocket) (message: string) = 
        task {
            let { Id = id; Socket = socket } = leasedSocket
            let data = Encoding.UTF8.GetBytes(message)
            let seg = new ArraySegment<byte>(data)
            if socket.State = WebSocketState.Open then
                printfn "To %A sending %s at %A" id message DateTime.Now
                do! socket.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None)
            else
                printfn "Closed %A at %A" leasedSocket.Id DateTime.Now
                sockets.TryRemove id |> ignore
                leasedSocket.Release ()
        }

let private receive (s: WebSocket) (ct: CancellationToken) = async {
    let buffer = new ArraySegment<byte>(Array.zeroCreate 4096)
    use ms = new MemoryStream ()

    let rec loadBuffer () = async {
        ct.ThrowIfCancellationRequested ()
        let! receipt = s.ReceiveAsync(buffer, ct)
                       |> Async.AwaitTask
        let time = DateTime.Now

        ms.Write (buffer.Array, buffer.Offset, receipt.Count)

        if receipt.EndOfMessage then return (receipt, time)
        else return! loadBuffer ()
    }

    let! loaded, time = loadBuffer ()

    return match loaded.MessageType with
           | WebSocketMessageType.Text ->
               ms.Seek(0L, SeekOrigin.Begin) |> ignore
               use reader = new StreamReader(ms, Encoding.UTF8)
               let str = reader.ReadToEnd()  

               // Not sure why these are being received               
               if str = "[object MessageEvent]" then
                NothingReceived ()
               else 
                TextReceived { Text = str 
                               Timestamp = time }
           | WebSocketMessageType.Close ->
               CloseReceived { CloseStatus = loaded.CloseStatus.Value
                               CloseStatusDescription = loaded.CloseStatusDescription 
                               Timestamp = time } 
           | _ -> 
               NothingReceived ()
}

let private registerSocket s =
    sockets.TryAdd (s.Id, s)
    |> ignore

    let rec listen () = async {
        let! ct = Async.CancellationToken
        let! received = receive s.Socket ct

        match received with
        | TextReceived t ->
            printfn "Received message at %s %s" (t.Timestamp.ToString()) t.Text
            let! again = listen () 
            return again
        | CloseReceived cr -> 
            printfn "Received close request at %s" (cr.Timestamp.ToString())
            let! _close = s.Socket.CloseAsync(cr.CloseStatus, cr.CloseStatusDescription, CancellationToken.None)
                          |> Async.AwaitTask
            return ()
        | NothingReceived _ ->
            let! again = listen () 
            return again
    }

    listen ()

let SendToAll msg = task {
    for s in sockets do
        try
            do! sendMessage s.Value msg
        with
            | _ -> sockets.TryRemove s.Key |> ignore
}


let Handshake (next: HttpFunc) (ctx: HttpContext) = task { 
    //
    // TODO: Maintain a temporary registry of issued IDs
    //

    let newId = Guid.NewGuid()
    ctx.SetHttpHeader "x-connection-id" newId
    return Some ctx
}

let private (|ConnectionId|_|) (ctx: HttpContext) =
    //
    // TODO: Also validate that the specific ID is valid      
    //
    match ctx.TryGetQueryStringValue "id"
          |> Option.map Guid.TryParse with
    | Some (true, guid) when ctx.WebSockets.IsWebSocketRequest -> Some guid
    | _ -> None

let OpenWebSocket (next: HttpFunc) (ctx: HttpContext) = task { 
    match ctx with
    | ConnectionId id -> 
        let! s = ctx.WebSockets.AcceptWebSocketAsync()
                 |> Async.AwaitTask

        // One method of keeping the socket alive: create a task
        // completion source (i.e. a promise) and manage its state
        // independently. We could choose to just keep it open
        // indefinitely.
        let releaseSocket = new TaskCompletionSource<Object>();
        
        let socket = { Id = id
                       Socket = s
                       Release = (fun () -> releaseSocket.SetResult ()) }
        
        let! _registered = registerSocket socket

        let! _finished = releaseSocket.Task 
                         |> Async.AwaitTask
        
        printfn "Socket release: %A" id

        sockets.TryRemove id |> ignore

        return Some ctx
    | _ -> 
        ctx.SetStatusCode 400
        return Some ctx
}
