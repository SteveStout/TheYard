# Sealed by default

TheYard is a used-vehicle auction platform built in the open: a .NET 10 API, a React front end, and
two databases underneath it. A class in it is sealed unless something in the solution derives from it.
That is the default rather than a decision taken class by class, and the one class that had slipped
past it was found by counting, not by review, which is the argument for the test at the bottom of this
page.

## What `sealed` says

`sealed` on a class stops any other class deriving from it. The C# reference puts it in one line:
"When applied to a class, the `sealed` modifier prevents other classes from inheriting from it"
([`sealed`, C# reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/sealed)).

A whole class from this repository, the in-memory half of the password reset port, as it is written:

```live path=api/TheYard.Application/ResetLinks.cs region=memory
```

Nothing derives from it and nothing was meant to, and the declaration says so before a reader has to
go looking for a subclass that is not there.

## Sealed records, and the comparison they give you

This is the part worth passing on, and it is the reason every record in this repository is sealed on
purpose.

**A record is a class the compiler writes equality for.** Two records holding the same field values
compare as equal; two ordinary classes holding the same field values do not, because a class compares
references and a record compares members. That difference is the whole reason to reach for a record:
comparing payloads in a test, using a data transfer object as a dictionary key, or deciding whether
anything actually changed.

```csharp
public sealed class PlainBid
{
    public required string VehicleId { get; init; }
    public required int Amount { get; init; }
}

public sealed record Bid(string VehicleId, int Amount);

var a = new PlainBid { VehicleId = "v-1", Amount = 9_000 };
var b = new PlainBid { VehicleId = "v-1", Amount = 9_000 };
Console.WriteLine(a == b); // False. Two references, and they are not the same reference.

Console.WriteLine(new Bid("v-1", 9_000) == new Bid("v-1", 9_000)); // True. Same values, same thing.

// Which is also why one of these works as a key and the other does not.
var seen = new HashSet<Bid> { new("v-1", 9_000) };
Console.WriteLine(seen.Contains(new Bid("v-1", 9_000))); // True.
```

**Leave that record unsealed and the compiler hands it something else as well: a `protected virtual
EqualityContract`.** That property is how the generated `Equals` decides whether the object it was
handed is even the same kind of thing, and the C# reference is explicit that it exists for exactly
that
([Records, C# reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record)).
Derive from your DTO and the compiler overrides the contract for you. Every comparison made against
the base type now means something different, no call site changed, and the place that shows up is
production.

```csharp
public record Payload(string Id);

public record Audited(string Id, string By) : Payload(Id);

Payload one = new Payload("p-1");
Payload two = new Audited("p-1", "steve");

// False, and nothing at the call site says why: the derived record overrode
// EqualityContract, so "same kind of thing" is no longer true.
Console.WriteLine(one == two);
Console.WriteLine(one.Equals(two));
```

Sealing the record removes that case from the language rather than from a reviewer's memory. Same
values, same thing, and nothing can be derived that changes the answer.

The wire shape of a vehicle, whole, as the site serves it:

```live path=api/TheYard.Data/Vehicle.cs region=*
```

Two things in that file are worth the read. The sealing reason is written on the type itself rather
than left to a page, and the comment is honest about the exception: two of the properties are
`IReadOnlyList<string>`, and the default comparer for an interface calls the instance's own `Equals`,
which for a list is reference equality. Twenty-seven fields compare by value and two compare by
identity, and a test that needs those two compared by contents has to say so.

Record structs cannot be derived from at all, so they get the same guarantee from the runtime.

**Habits do not carry from one busy month to the next, which is why this is a test rather than a
rule.** Every build checks it, and anything that has to stay open goes on a named allow-list with the
reason written beside it. The test is at the bottom of this page, whole.

## Why this codebase seals by default

Intent comes first. An open class is an invitation: somebody may reasonably derive from it, override
a method, and expect the rest of the code to keep working. These classes were not written to survive
that. Most of them do one job behind a port, like the null sender that stands in when a container has
no mail configured:

```live path=api/TheYard.Application/Email.cs region=email-port
```

The interface is the extension point, and the class behind it is finished. That is the shape almost
everything here takes, and sealing is how the class says which of the two it is.

The runtime gets something out of it as well. When the just-in-time compiler knows a type has no
subclass, it can turn virtual calls into direct ones, and casts, type checks, array assignments and
span conversions all get cheaper. Gérald Barré measured each of those
([Performance benefits of sealed class in .NET](https://www.meziantou.net/performance-benefits-of-sealed-class.htm)).
His numbers are his, from his machine, and none of them are copied onto this page: a figure this
project cannot measure through its own gate is a figure it should not print.

An analyzer catches the half a person forgets. CA1852 names an internal class that nothing derives
from and that could be sealed
([CA1852: Seal internal types](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1852)).
It is on here as a warning:

```live path=.editorconfig region=ca1852
```

The gate reads any warning as red, so that rule is held by the build rather than by anybody's
attention. CA1852 stops at internal classes, which is why the test below covers the public ones too.

The same class open and sealed, and what a caller can do with each:

```csharp
// Open. Anything can derive from it, and the override is invisible at the call
// site: a method that takes an Increment has no idea which one it is holding.
public class Increment
{
    public virtual int For(int standing) => standing < 10_000 ? 250 : 1_000;
}

public sealed class Generous : Increment
{
    // Every rule downstream now reads a different number, and no call site changed.
    public override int For(int standing) => 1;
}

// Sealed. The class is the whole answer, the call is a direct one, and the
// subclass above does not compile.
public sealed class SealedIncrement
{
    public int For(int standing) => standing < 10_000 ? 250 : 1_000;
}
```

## When not to seal

The Framework Design Guidelines say the opposite of this page: "DO NOT seal classes without having a
good reason"
([Sealing](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/sealing)). That advice
is written for public libraries, where a type other people derive from is part of the contract and
sealing it later breaks strangers who already shipped. Nothing here is a library. These five projects
are one application, everything that derives from anything is in the same solution, and unsealing a
class the day somebody needs to is one keyword in one commit.

So the rule carries its exception in writing. A class with a subclass stays open. A class that has to
stay open for a reason the code cannot show goes on a named allow-list inside the test, with the
reason beside it, which is a decision somebody made rather than one nobody noticed.

The one base class in this repository written to be inherited is a test base that three test classes
share
([`api/TheYard.Tests/AuthTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AuthTests.cs)).
It is abstract, which says the same thing from the other side: this class exists to be derived from
and cannot be used on its own. A base written to be open looks like this:

```csharp
// Written to be inherited: abstract, so it cannot be used on its own; one member
// a subclass owes it; and the work every subclass shares sitting in the base
// where all of them get it.
public abstract class StoreCheck
{
    protected StoreCheck(TimeProvider clock) => Clock = clock;

    protected TimeProvider Clock { get; }

    /// <summary>The one thing a subclass has to answer.</summary>
    protected abstract Task<bool> IsReachableAsync(CancellationToken cancellation);

    /// <summary>What every check reports, in the shape the Admin tab reads.</summary>
    public async Task<(string Name, bool Healthy, long At)> RunAsync(CancellationToken cancellation)
    {
        bool reachable = await IsReachableAsync(cancellation);
        return (GetType().Name, reachable, Clock.GetUtcNow().ToUnixTimeMilliseconds());
    }
}
```

## The test that holds it

One test class reads the five projects that make up the application, counts the shape of every class
and record the build actually has, and fails when one of them is open with nothing deriving from it:

```live path=api/TheYard.Tests/SealedByDefaultTests.cs region=*
```

Measured on this build:

| shape | count |
| --- | --- |
| sealed | 79 |
| static | 39 |
| abstract | 0 |
| open | 0 |
| records | 75 |
| open records | 0 |

79 of the 118 classes in those five projects are sealed, and the 39 that are not are static, which
cannot be inherited either. All 75 records are sealed or record structs, so value comparison means
what it says on every one of them. Every number in that table is read back out of this page by the
test above, so it cannot drift from the build the way a number typed once always does.

## References

- [`sealed`, C# reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/sealed):
  what the keyword does and where it can go.
- [Records, C# reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record):
  the equality the compiler writes, and the `EqualityContract` an open record compares first.
- [CA1852: Seal internal types](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1852):
  the analyzer this repository turns on, and the internal classes it covers.
- [Framework Design Guidelines: Sealing](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/sealing):
  the case for leaving classes open, and the library context it is written for.
- [Gérald Barré, Performance benefits of sealed class in .NET](https://www.meziantou.net/performance-benefits-of-sealed-class.htm):
  the measurements behind the runtime paragraph, including the cases the JIT cannot help with.
