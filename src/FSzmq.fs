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

type Machine = | Localhost
type Network = | Localhost

module Agent =
  type T<'t> = MailboxProcessor<'t>
  module Socket =
    type S = fszmq.Socket * System.Threading.SynchronizationContext
    type Connection = | Network of Network * int | Machine of Machine * int
    let connect (c:Connection) (s:fszmq.Socket) =
      c |> function
        | Network (n, p) -> fszmq.Socket.bind s (sprintf "tcp://127.0.0.1:%d" p)
        | Machine (m, p) -> fszmq.Socket.connect s (sprintf "tcp://127.0.0.1:%d" p)
      s, System.Threading.SynchronizationContext.Current
    let send (s:S) msg = async { do! Async.SwitchToContext (snd s)
                                 fszmq.Socket.send (fst s) msg }
    let recv (s:S) = async { do! Async.SwitchToContext (snd s)
                             return fszmq.Socket.recv (fst s) }

    let ensureSocket (f:unit->S) (s:S option) = s |> Option.orLazyDefault f
    let rec receiver<'t> f s (t:T<'t>) = async {
      let s = s |> ensureSocket f
      try
        do! s |> recv |> Async.map (Message.toT<'t> >> t.Post)
        return! receiver f (Some s) t
      with
        | :? fszmq.ZMQError as e when e.Message = "Interrupted system call" -> return! receiver f (Some s) t
        | e -> printfn "receiver<%A>: %A" typeof<'t> e; raise e
      }
    let rec sender<'t> f s (t:T<'t>) = async {
      let s = s |> ensureSocket f
      try
        do! t.Receive () |> Async.bind (Message.ofT<'t> >> send s)
        return! sender f (Some s) t
      with
        | :? fszmq.ZMQError as e when e.Message = "Interrupted system call" -> return! sender f (Some s) t
        | e -> printfn "sender<%A>: %A" typeof<'t> e; raise e
      }
    type ReqRep<'t,'u> = 't * AsyncReplyChannel<'u>
    let rec replyer<'t,'u> f s (callback:'t->'u Async) (t:T<ReqRep<'t,'u>>) = async {
      let s = s |> ensureSocket f
      try
        let! request = s |> recv
        let! reply = request |> Message.toT<'t> |> callback
        do! reply |> Message.ofT<'u> |> send s
        return! replyer f (Some s) callback t
      with
        | :? fszmq.ZMQError as e when e.Message = "Interrupted system call" -> return! replyer f (Some s) callback t
        | e -> printfn "replyer<%A,%A>: %A" typeof<'t> typeof<'u> e; raise e
      }
    let rec requester<'t,'u> f s (t:T<ReqRep<'t,'u>>) = async {
      let s = s |> ensureSocket f
      try
        let! request, reply = t.Receive ()
        do! request |> Message.ofT<'t> |> send s
        let! result = s |> recv
        result |> Message.toT<'u> |> reply.Reply
        return! requester f (Some s) t
      with
        | :? fszmq.ZMQError as e when e.Message = "Interrupted system call" -> return! requester f (Some s) t
        | e -> printfn "requester<%A,%A>: %A" typeof<'t> typeof<'u> e; raise e
      }
    let pull (c:Context.T) (m:Machine) (port:int) () =
      fszmq.Context.pull c |> connect (Machine (m, port))
    let push (c:Context.T) (n:Network) (port:int) () =
      fszmq.Context.push c |> connect (Network (n, port))
    let subscribe (c:Context.T) (m:Machine) (port:int) () =
      fszmq.Context.sub c |> Do (fun s -> fszmq.Socket.subscribe s [| [||] |]) |> connect (Machine (m, port))
    let publish (c:Context.T) (n:Network) (port:int) () =
      fszmq.Context.pub c |> connect (Network (n, port))
    let request (c:Context.T) (m:Machine) (port:int) () =
      fszmq.Context.req c |> connect (Machine (m, port))
    let reply (c:Context.T) (n:Network) (port:int) () =
      fszmq.Context.rep c |> connect (Network (n, port))

  let startPuller<'t> (c:Context.T) (m:Machine) (port:int) : T<'t> = T<'t>.Start (Socket.receiver<'t> (Socket.pull c m port) None)
  let startPusher<'t> (c:Context.T) (n:Network) (port:int) : T<'t> = T<'t>.Start (Socket.sender<'t> (Socket.push c n port) None)
  let startSubscriber<'t> (c:Context.T) (m:Machine) (port:int) : T<'t> = T<'t>.Start (Socket.receiver<'t> (Socket.subscribe c m port) None)
  let startPublisher<'t> (c:Context.T) (n:Network) (port:int) : T<'t> = T<'t>.Start (Socket.sender<'t> (Socket.publish c n port) None)
  let startRequester<'t,'u> (c:Context.T) (m:Machine) (port:int) : T<Socket.ReqRep<'t,'u>> = T<Socket.ReqRep<'t,'u>>.Start (Socket.requester<'t,'u> (Socket.request c m port) None)
  let startReplyer<'t,'u> (c:Context.T) (n:Network) (port:int) (callback:'t->'u Async) : T<Socket.ReqRep<'t,'u>> = T<Socket.ReqRep<'t,'u>>.Start (Socket.replyer<'t,'u> (Socket.reply c n port) None callback)
  let send<'t> (message:'t) (t:T<'t>) : unit = t.Post message
  let receive<'t> (t:T<'t>) : 't Async = t.Receive ()
  let request<'t,'u> (message:'t) (t:T<Socket.ReqRep<'t,'u>>) : 'u Async = t.PostAndAsyncReply (fun c -> message, c)
