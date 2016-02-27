module Tests

module Unit =
  type TestAttribute = NUnit.Framework.TestAttribute

module Property =
  type TestAttribute = NUnit.Framework.TestAttribute
  let property = FsCheck.Check.QuickThrowOnFailure

  let [<Test>] ``A message serialized and deserialized stays the same`` () =
    property (fun x -> (FSzmq.Message.ofT >> FSzmq.Message.toT) x = x)

  let [<Test>] ``A message sent through an agent is received`` () =
    use context = FSzmq.Context.create ()
    let subscriber = FSzmq.Agent.subscribe context FSzmq.Machine.Localhost 12345
    let publisher = FSzmq.Agent.publish context FSzmq.Network.Localhost 12345
    property (fun x -> publisher |> FSzmq.Agent.send x; true)
    if subscriber |> FSzmq.Agent.receiveMany |> List.length = 100 then () else failwith "Not all messsages received"
