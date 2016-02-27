namespace FSzmq

module Message =
  type T = byte array
  type BinaryFormatter = System.Runtime.Serialization.Formatters.Binary.BinaryFormatter
  type MemoryStream = System.IO.MemoryStream

  let ofT<'t> (x:'t) : T =
    if box x = null then [||]
    else
      let binFormatter = new BinaryFormatter()
      use stream = new MemoryStream()
      binFormatter.Serialize(stream, x)
      stream.ToArray()

  let toT<'t> (t:T) : 't =
    if Array.length t = 0 then unbox null
    else
      let binFormatter = new BinaryFormatter()

      use stream = new MemoryStream(t)
      binFormatter.Deserialize(stream) :?> 't
