namespace FSzmq

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
    let serialize (x:obj) (ms:MemoryStream.T) (t:T) = t.Serialize (ms, x)
    let deserialize (ms:MemoryStream.T) (t:T) = t.Deserialize ms

  let ofT<'t> (x:'t) : T =
    if box x = null then [||]
    else
      use stream = MemoryStream.create ()
      BinaryFormatter.create () |> BinaryFormatter.serialize x stream
      MemoryStream.toArray stream

  let toT<'t> (t:T) : 't =
    if Array.length t = 0 then unbox null
    else
      use stream = MemoryStream.ofArray t
      BinaryFormatter.create () |> BinaryFormatter.deserialize stream |> unbox
