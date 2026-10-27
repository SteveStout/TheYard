**You can see all five SOLID principles in five small files.**

SOLID is five rules that keep code easy to change. The names are hard to recite. The habits are easy to spot once you know where to look.

Here they are in [The Shed](https://theshed.stevenstout.biz), a small file browser I built in .NET 10 for people learning the stack. Each principle gets the rule in plain words, then the real file that follows it. The slides below walk the same five, one per principle.

## 1. Single Responsibility (SRP)

A class should have one job and one reason to change.

In The Shed: [FileBrowser](https://github.com/SteveStout/TheYard/blob/main/samples/theshed/Application/FileBrowser.cs) runs the search. [NamePattern](https://github.com/SteveStout/TheYard/blob/main/samples/theshed/Domain/NamePattern.cs) decides what matches. [SearchMatches](https://github.com/SteveStout/TheYard/blob/main/samples/theshed/Domain/SearchMatches.cs) decides when to stop. Three jobs, three classes.

## 2. Open/Closed (OCP)

Open for extension, closed for modification. Add new behavior without rewriting what already works.

In The Shed: a new kind of refusal is one new line in [ApiRefusalException](https://github.com/SteveStout/TheYard/blob/main/samples/theshed/Application/ApiRefusalException.cs). No existing class changes.

## 3. Liskov Substitution (LSP)

Anything that implements an interface must be swappable for any other implementation without breaking the caller.

In The Shed: the tests swap [FakeStore](https://github.com/SteveStout/TheYard/blob/main/samples/theshed/tests/TestProject.Tests/SampleTree.cs) in for [PhysicalFileStore](https://github.com/SteveStout/TheYard/blob/main/samples/theshed/Infrastructure/PhysicalFileStore.cs). FileBrowser never notices.

## 4. Interface Segregation (ISP)

No class should depend on methods it does not use. Small, specific interfaces beat one big general one.

In The Shed: [IFileStore](https://github.com/SteveStout/TheYard/blob/main/samples/theshed/Application/IFileStore.cs) has ten methods, every one a file or folder operation. No web, no config, no logging leaks in.

## 5. Dependency Inversion (DIP)

High-level code should depend on abstractions, not on concrete implementations.

In The Shed: FileBrowser takes `IFileStore`, the interface. The one line that knows about the real disk is in the [wiring](https://github.com/SteveStout/TheYard/blob/main/samples/theshed/Composition/FilesRegistration.cs).

## Why it matters

- Lower coupling → a change stays where you made it
- Higher testability → each piece tests on its own
- Easier maintenance → clear ownership, fewer bugs, fewer merge conflicts

## A simple way to remember

- One job per class → S
- Add a line, not a rewrite → O
- Swap the real for the fake → L
- Ask for only what you need → I
- Depend on the interface → D

The names matter less than the habit. Code can follow all five before you can recite them.

Which of the five do you catch yourself breaking most often?

## References

- [The Shed, live](https://theshed.stevenstout.biz)
- [The Shed's code, samples/theshed on GitHub](https://github.com/SteveStout/TheYard/tree/main/samples/theshed)
- [The LinkedIn post](https://www.linkedin.com/feed/update/urn:li:activity:7518039721311232000/)
- [TheYard on GitHub](https://github.com/SteveStout/TheYard)
