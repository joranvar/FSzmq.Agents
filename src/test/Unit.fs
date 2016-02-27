module Unit
type TestAttribute = NUnit.Framework.TestAttribute

[<Test>] let success () = () ;;
[<Test>] let fail () = if true then failwith "Wrong" else () ;;
