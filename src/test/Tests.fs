module Tests

module Unit =
  type TestAttribute = NUnit.Framework.TestAttribute

module Exploratory =
  type TestAttribute = NUnit.Framework.TestAttribute

  let [<Test>] ``An address looks right`` () =
    printfn "%s"
      (FSzmq.Connection.Network (FSzmq.Connection.LocalhostNetwork, 987) |> FSzmq.Connection.toString)
    printfn "%s"
      (FSzmq.Connection.Network (FSzmq.Connection.InterNetwork, 654) |> FSzmq.Connection.toString)
    printfn "%s"
      (FSzmq.Connection.Machine (FSzmq.Connection.Localhost, 123) |> FSzmq.Connection.toString)
    printfn "%s"
      (FSzmq.Connection.Machine (FSzmq.Connection.Other (System.Net.IPAddress [| 84uy; 65uy; 187uy; 45uy |]), 123) |> FSzmq.Connection.toString)

module Property =
  type TestAttribute = NUnit.Framework.TestAttribute
  type PerformanceTestAttribute () =
    //inherit NUnit.Framework.TestAttribute ()
    inherit NUnit.Framework.IgnoreAttribute ("Performance test")

  let property = FsCheck.Check.QuickThrowOnFailure

  let [<Test>] ``A message serialized and deserialized stays the same`` () =
    property (fun x -> (FSzmq.Message.ofT >> FSzmq.Message.toT) x = x)

  let context = FSzmq.Context.create ()

  let [<Test>] ``A message sent through an agent stays the same`` () =
    let puller = FSzmq.Agent.startPuller context FSzmq.Connection.Localhost 12345
    let pusher = FSzmq.Agent.startPusher context FSzmq.Connection.LocalhostNetwork 12345
    property (fun x -> pusher |> FSzmq.Agent.send x
                       puller |> FSzmq.Agent.receive |> Async.RunSynchronously = x)

  let [<Test>] ``A message sent through a publisher agent can be received`` () =
    let subscriber = FSzmq.Agent.startSubscriber context FSzmq.Connection.Localhost 12346
    let publisher = FSzmq.Agent.startPublisher context FSzmq.Connection.LocalhostNetwork 12346
    Async.Sleep 3000 |> Async.RunSynchronously
    property (fun x -> publisher |> FSzmq.Agent.send x; true)
    property (fun x -> publisher |> FSzmq.Agent.send x; true)
    subscriber |> FSzmq.Agent.receive |> Async.RunSynchronously |> ignore

  let [<Test>] ``A message sent through a request agent gets the expected reply`` () =
    let replyer = FSzmq.Agent.startReplyer context FSzmq.Connection.LocalhostNetwork 12348 (fun i -> async {return i + 1})
    let requester = FSzmq.Agent.startRequester context FSzmq.Connection.Localhost 12348
    property (fun x -> requester |> FSzmq.Agent.request x |> Async.RunSynchronously = x + 1)

  let [<PerformanceTest>] ``Creating many agents will work easily`` () =
    let numAgents = 90 (* actually twice as much *)
    let agents =
      [0uy..(byte numAgents)]
      |> List.map (fun i -> i, ( FSzmq.Agent.startPusher context FSzmq.Connection.LocalhostNetwork (int i + 12000)
                               , FSzmq.Agent.startPuller context FSzmq.Connection.Localhost (int i + 12000) ))
      |> Map.ofList
    property (fun b x -> let (pusher, puller) = agents |> Map.find (byte (abs (b % numAgents)))
                         pusher |> FSzmq.Agent.send x
                         puller |> FSzmq.Agent.receive |> Async.RunSynchronously = x)
