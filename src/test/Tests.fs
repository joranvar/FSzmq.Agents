module Tests

module Unit =
  type TestAttribute = NUnit.Framework.TestAttribute

  let [<Test>] success () = ()
  let [<Test>] fail () = if true then failwith "Wrong" else ()

module Property =
  type TestAttribute = NUnit.Framework.TestAttribute
  let property = FsCheck.Check.QuickThrowOnFailure

  let [<Test>] ``A list reversed twice stays the same`` () =
    property (fun l -> (List.rev << List.rev) l = l)
