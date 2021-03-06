namespace FSzmq

[<AutoOpen>]
module Utils =
  let Do f x = f x |> ignore ; x
  let DisposingDo f (x:#System.IDisposable) = let result = f x in x.Dispose () ; result

module Async =
  let map f x = async { let! result = x in return f result }
  let bind f x = async { let! result = x in return! f result }

module Option =
  let orLazyDefault f = function None -> f () | Some v -> v

module Message =
  type T = byte array

  module MemoryStream =
    type T = System.IO.MemoryStream
    let create () : T = new T ()
    let ofArray (arr:byte array) : T = new T (arr)
    let toArray (t:T) : byte array = t.ToArray ()

  module BinaryFormatter =
    type T = System.Runtime.Serialization.Formatters.Binary.BinaryFormatter
    let create () : T = T ()
    let serialize (x:obj) (ms:MemoryStream.T) = (create ()).Serialize (ms, x)
    let deserialize (ms:MemoryStream.T) = (create ()).Deserialize ms

  let ofT<'t> (x:'t) : T =
    if box x = null then [||]
    else MemoryStream.create () |> Do (BinaryFormatter.serialize x) |> DisposingDo MemoryStream.toArray

  let toT<'t> (t:T) : 't =
    if Array.length t = 0 then unbox null
    else t |> MemoryStream.ofArray |> DisposingDo BinaryFormatter.deserialize |> unbox

module Context =
  type T = fszmq.Context
  let create () = new T ()

module Connection =
  type Network = | LocalhostNetwork | InterNetwork
  type Machine = | Localhost | Other of System.Net.IPAddress
  type T = | Network of Network * int | Machine of Machine * int
  let localIP =
    let localHost = System.Net.Dns.GetHostName()
    System.Net.Dns.GetHostEntry(localHost).AddressList
    |> Array.find (fun x -> x.AddressFamily.ToString() = "InterNetwork")
  let toString = function
    | Machine (Localhost, p)
    | Network (LocalhostNetwork, p)-> sprintf "tcp://127.0.0.1:%d" p
    | Machine (Other ip, p)-> sprintf "tcp://%A:%d" ip p
    | Network (InterNetwork, p)-> sprintf "tcp://%A:%d" localIP p

module Agent =
  type T<'t> = MailboxProcessor<'t>
  module Socket =
    type S = fszmq.Socket * System.Threading.SynchronizationContext
    type Type = | Pull | Push | Sub of Message.T list | Pub | Req | Rep

    let create (t:Type) (c:Context.T) =
      let subscribe (channels:Message.T list) (s:fszmq.Socket) = fszmq.Socket.subscribe s (channels |> Array.ofList); s
      c |> match t with
           | Pull -> fszmq.Context.pull
           | Push -> fszmq.Context.push
           | Sub channels -> fszmq.Context.sub >> subscribe channels
           | Pub -> fszmq.Context.pub
           | Req -> fszmq.Context.req
           | Rep -> fszmq.Context.rep
       , System.Threading.SynchronizationContext.Current
    let connect (c:Connection.T) (s:S) =
      let address = c |> Connection.toString
      c |> function
        | Connection.Network (n, p) -> fszmq.Socket.bind (fst s) address
        | Connection.Machine (m, p) -> fszmq.Socket.connect (fst s) address
      s
    let pull (c:Context.T) (m:Connection.Machine) (port:int) () = c |> create Pull |> connect (Connection.Machine (m, port))
    let push (c:Context.T) (n:Connection.Network) (port:int) () = c |> create Push |> connect (Connection.Network (n, port))
    let subscribe (c:Context.T) (m:Connection.Machine) (port:int) () = c |> create (Sub [ ""B ]) |> connect (Connection.Machine (m, port))
    let publish (c:Context.T) (n:Connection.Network) (port:int) () = c |> create Pub |> connect (Connection.Network (n, port))
    let request (c:Context.T) (m:Connection.Machine) (port:int) () = c |> create Req |> connect (Connection.Machine (m, port))
    let reply (c:Context.T) (n:Connection.Network) (port:int) () = c |> create Rep |> connect (Connection.Network (n, port))

    let send (s:S) msg = async { do! Async.SwitchToContext (snd s)
                                 fszmq.Socket.send (fst s) msg }
    let recv (s:S) = async { do! Async.SwitchToContext (snd s)
                             return fszmq.Socket.recv (fst s) }

    let ensureSocket (f:unit->S) (s:S option) =
      try s |> Option.orLazyDefault f with e -> printfn "ensureSocket: %A" e; raise e
    let handleZMQInterrupt (s:string) = box >> function
      | :? fszmq.ZMQError as e when e.Message = "Interrupted system call" -> ()
      | e -> printfn "%s: %A" s e; e |> unbox |> raise

    let rec receiver<'t> f s (t:T<'t>) = async {
      let s = s |> ensureSocket f
      try do! s |> recv |> Async.map (Message.toT<'t> >> t.Post)
      with e -> handleZMQInterrupt (sprintf "receiver<%A>" typeof<'t>) e
      return! receiver f (Some s) t
    }
    let rec sender<'t> f s (t:T<'t>) = async {
      let s = s |> ensureSocket f
      try do! t.Receive () |> Async.bind (Message.ofT<'t> >> send s)
      with e -> handleZMQInterrupt (sprintf "sender<%A>" typeof<'t>) e
      return! sender f (Some s) t
      }

    type ReqRep<'t,'u> = 't * AsyncReplyChannel<'u>
    let rec replyer<'t,'u> f s (callback:'t->'u Async) (t:T<ReqRep<'t,'u>>) = async {
      let s = s |> ensureSocket f
      try
        let! request = s |> recv
        let! reply = request |> Message.toT<'t> |> callback
        do! reply |> Message.ofT<'u> |> send s
      with e -> handleZMQInterrupt (sprintf "replyer<%A,%A>" typeof<'t> typeof<'u>) e
      return! replyer f (Some s) callback t
      }
    let rec requester<'t,'u> f s (t:T<ReqRep<'t,'u>>) = async {
      let s = s |> ensureSocket f
      try
        let! request, reply = t.Receive ()
        do! request |> Message.ofT<'t> |> send s
        let! result = s |> recv
        result |> Message.toT<'u> |> reply.Reply
      with e -> handleZMQInterrupt (sprintf "requester<%A,%A>" typeof<'t> typeof<'u>) e
      return! requester f (Some s) t
      }

  let startPuller<'t> (c:Context.T) (m:Connection.Machine) (port:int) : T<'t> = T<'t>.Start (Socket.receiver<'t> (Socket.pull c m port) None)
  let startPusher<'t> (c:Context.T) (n:Connection.Network) (port:int) : T<'t> = T<'t>.Start (Socket.sender<'t> (Socket.push c n port) None)
  let startSubscriber<'t> (c:Context.T) (m:Connection.Machine) (port:int) : T<'t> = T<'t>.Start (Socket.receiver<'t> (Socket.subscribe c m port) None)
  let startPublisher<'t> (c:Context.T) (n:Connection.Network) (port:int) : T<'t> = T<'t>.Start (Socket.sender<'t> (Socket.publish c n port) None)
  let startRequester<'t,'u> (c:Context.T) (m:Connection.Machine) (port:int) : T<Socket.ReqRep<'t,'u>> = T<Socket.ReqRep<'t,'u>>.Start (Socket.requester<'t,'u> (Socket.request c m port) None)
  let startReplyer<'t,'u> (c:Context.T) (n:Connection.Network) (port:int) (callback:'t->'u Async) : T<Socket.ReqRep<'t,'u>> = T<Socket.ReqRep<'t,'u>>.Start (Socket.replyer<'t,'u> (Socket.reply c n port) None callback)

  let send<'t> (message:'t) (t:T<'t>) : unit = t.Post message
  let receive<'t> (t:T<'t>) : 't Async = t.Receive ()
  let request<'t,'u> (message:'t) (t:T<Socket.ReqRep<'t,'u>>) : 'u Async = t.PostAndAsyncReply (fun c -> message, c)
