namespace FSzmq

[<AutoOpen>]
module Utils =
  let Do f x = f x |> ignore ; x
  let DisposingDo f (x:#System.IDisposable) = let result = f x in x.Dispose () ; result

module Async =
  let map f x = async { let! result = x in return f result }

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
    type S = fszmq.Socket
    let ensureSocket (f:unit->S) (s:S option) = s |> Option.orLazyDefault f
    let rec receiver<'t> f s (t:T<'t>) = async {
      let s = s |> ensureSocket f
      try
        s |> fszmq.Socket.recv |> Message.toT<'t> |> t.Post
        return! receiver f (Some s) t
      with
        | :? System.Threading.ThreadInterruptedException -> return! receiver f (Some s) t
        | e -> printfn "%A" e
      }
    let rec sender<'t> f s (t:T<'t>) = async {
      let s = s |> ensureSocket f
      try
        do! t.Receive () |> Async.map (Message.ofT<'t> >> fszmq.Socket.send s)
        return! sender f (Some s) t
      with
        | :? System.Threading.ThreadInterruptedException -> return! sender f (Some s) t
        | e -> printfn "%A" e
      }
    let pull (c:Context.T) (m:Machine) (port:int) () =
      fszmq.Context.pull c
      |> Do (fun s -> fszmq.Socket.connect s (sprintf "tcp://127.0.0.1:%d" port))
    let push (c:Context.T) (n:Network) (port:int) () =
      fszmq.Context.push c
      |> Do (fun s -> fszmq.Socket.bind s (sprintf "tcp://127.0.0.1:%d" port))
    let subscribe (c:Context.T) (m:Machine) (port:int) () =
      fszmq.Context.sub c
      |> Do (fun s -> fszmq.Socket.subscribe s [| [||] |])
      |> Do (fun s -> fszmq.Socket.connect s (sprintf "tcp://127.0.0.1:%d" port))
    let publish (c:Context.T) (n:Network) (port:int) () =
      fszmq.Context.pub c
      |> Do (fun s -> fszmq.Socket.bind s (sprintf "tcp://127.0.0.1:%d" port))

  let pull<'t> (c:Context.T) (m:Machine) (port:int) : T<'t> = T<'t>.Start (Socket.receiver<'t> (Socket.pull c m port) None)
  let push<'t> (c:Context.T) (n:Network) (port:int) : T<'t> = T<'t>.Start (Socket.sender<'t> (Socket.push c n port) None)
  let subscribe<'t> (c:Context.T) (m:Machine) (port:int) : T<'t> = T<'t>.Start (Socket.receiver<'t> (Socket.subscribe c m port) None)
  let publish<'t> (c:Context.T) (n:Network) (port:int) : T<'t> = T<'t>.Start (Socket.sender<'t> (Socket.publish c n port) None)
  let send<'t> (message:'t) (t:T<'t>) : unit = t.Post message
  let receive<'t> (t:T<'t>) : 't Async = t.Receive ()
