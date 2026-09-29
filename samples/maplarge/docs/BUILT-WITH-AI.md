# Built with AI

How this project was made, as a working method rather than a disclosure. The decision record is
ADR-011; this page is the practice.

## The loop

1. **Decide the shape in writing.** What the brief asks, what the answer is, which practices come
   and which stay behind, the layout, the routes, the URL keys. For this project that was a
   two-page plan, written before any code and approved before any code.
2. **List the tests.** Every rule that has to survive gets a named test before the code it holds
   exists. ADR-009 is that list, kept current.
3. **Draft against the list.** The assistant (Claude) writes the first version of each file from
   the plan and the tests. Tests included.
4. **Build and run on a real machine.** Nothing is committed on the strength of the draft. The
   build is warnings-as-errors; the suites are `dotnet test` and `node --test tests/js`.
5. **Read everything.** The assistant reviews its own draft as three readers (a tester, a hiring
   manager, a staff engineer) and corrects; a person reads every file before the project is sent.
6. **Write the record.** Each decision gets its ADR with the code it decided about quoted live from
   the build, so the record cannot drift from the code.

## What it is good at

- The first draft of a class against a test that already exists.
- The twelfth test in a pattern the first eleven set.
- A record's first draft from a decision that was already made.
- A second reader on a diff, asked to find what would break.
- The tedious half of a change across many files.

## What stays with a person

- The decisions: the architecture, the wire, what a refusal costs, what to leave out.
- The tests' intent, even when the assistant types them.
- The last read before anything ships.

## Where to see it

The commit history is the loop as it happened: the starter as received, then the work in the
order it was built, each commit green. The records name what was decided on purpose.
