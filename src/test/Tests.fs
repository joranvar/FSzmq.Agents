module Tests

module Unit =
  type TestAttribute = NUnit.Framework.TestAttribute

module Property =
  type TestAttribute = NUnit.Framework.TestAttribute
  let property = FsCheck.Check.QuickThrowOnFailure

  let [<Test>] ``A message serialized and deserialized stays the same`` () =
    property (fun x -> (FSzmq.Message.ofT >> FSzmq.Message.toT) x = x)

  let [<Test>] ``A message sent through an agent stays the same`` () =
    let context = FSzmq.Context.create ()
    let puller = FSzmq.Agent.pull context FSzmq.Machine.Localhost 12345
    let pusher = FSzmq.Agent.push context FSzmq.Network.Localhost 12345
    property (fun x -> pusher |> FSzmq.Agent.send x
                       puller |> FSzmq.Agent.receive |> Async.RunSynchronously = x)
