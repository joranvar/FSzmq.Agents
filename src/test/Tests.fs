module Tests
type TestAttribute = NUnit.Framework.TestAttribute

let [<Test>] success () = ()
let [<Test>] fail () = if true then failwith "Wrong" else ()
