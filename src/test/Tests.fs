module Tests

module Unit =
  type TestAttribute = NUnit.Framework.TestAttribute

module Property =
  type TestAttribute = NUnit.Framework.TestAttribute
  let property = FsCheck.Check.QuickThrowOnFailure

  let [<Test>] ``A message serialized and deserialized stays the same`` () =
    property (fun x -> (FSzmq.Message.ofT >> FSzmq.Message.toT) x = x)

  let context = FSzmq.Context.create ()

  let [<Test>] ``A message sent through an agent stays the same`` () =
    let puller = FSzmq.Agent.pull context FSzmq.Machine.Localhost 12345
    let pusher = FSzmq.Agent.push context FSzmq.Network.Localhost 12345
    property (fun x -> pusher |> FSzmq.Agent.send x
                       puller |> FSzmq.Agent.receive |> Async.RunSynchronously = x)

  let [<Test>] ``A message sent through a publisher agent can be received`` () =
    let subscriber = FSzmq.Agent.subscribe context FSzmq.Machine.Localhost 12346
    let publisher = FSzmq.Agent.publish context FSzmq.Network.Localhost 12346
    property (fun x -> publisher |> FSzmq.Agent.send x; true)
    property (fun x -> publisher |> FSzmq.Agent.send x; true)
    subscriber |> FSzmq.Agent.receive |> Async.RunSynchronously
    ()
