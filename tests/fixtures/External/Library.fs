namespace Fixture.External

module UsesPackage =
    let run () = Expecto.Expect.isTrue (System.DateTime.Now.Year > 0) "ok"
