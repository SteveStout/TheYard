**AI beats most developers at typing code, and loses to good ones at judgment.**

The bet on replacing engineers is the wrong bet. My own multiplier with AI is about 1.5x, and none of it came from the tool.

On October 21, Dave Farley put out a video on his Modern Software Engineering channel, [I Said This Years Ago: Why Prompting AI Code Fails (And What Works)](https://www.youtube.com/watch?v=EkH_uoybj8k), that says why. AI changed the cost of one activity, producing code. It did nothing for the hard problems around it: saying what you want, proving you got it, moving in small steps.

The same week, the merger behind my job-search tracker moved from a PowerShell script on my laptop to C# in Azure, with AI drafting the code against tests I wrote first. Here are his five steps, each one tied to a real file in [TheYard](https://github.com/SteveStout/TheYard). The slides below walk the same five, one file per slide.

## 1. Write the spec first

One scenario, in the language of the problem, before the agent starts. Say what the system must do, not how.

In TheYard: the whole spec is one test name, `The_cloud_merger_leaves_the_same_bytes_as_the_laptop_merger`, in [ParityTests.cs](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt.Tests/ParityTests.cs).

## 2. Watch it go red

Red is the proof that your spec can catch a wrong answer. A test written after the code only proves the code does what it already does.

In TheYard: `Assert.Equal` on the master file, the moved-out file and every spool folder. 1,743,354 bytes and 79,903 bytes, identical.

## 3. Let the agent make it green

The agent owns the how. Read its code when the risk warrants it, not because every line demands it.

In TheYard: the rules talk to a port, `IMergeStore`. Nothing in them knows the share exists.

## 4. Keep the spec out of reach

The tests are the contract. A change to one is a change to the contract, and a person approves it.

In TheYard: every push to main runs the Function's tests on a clean GitHub runner.

## 5. Small steps

One scenario: red, green, review, commit. Small steps are how you find out you are wrong while it is still cheap to be wrong.

In TheYard: the same five input files ran on Azure in a scratch folder first and gave the same SHA-256, the file's fingerprint. Then the hand-over was one setting, `MERGER=on`. The numbers are in the [decision record](https://theyard.stevenstout.biz/?doc=adr-the-tracker-merger-runs-in-azure).

## A simple way to remember

- Say what, not how → Specify
- Make it fail before it passes → Verify
- Let the agent own the how → Green
- Tests are the contract → Protect them
- One scenario at a time → Small steps

Farley's point that stuck with me: the agent can draft the tests, and a person who understands the problem decides which ones count. That part is the job.

Who on your team writes the check that proves the code did what was asked?

## References

- Dave Farley, [I Said This Years Ago: Why Prompting AI Code Fails (And What Works)](https://www.youtube.com/watch?v=EkH_uoybj8k), Modern Software Engineering on YouTube, October 21, 2026
- [Dave Farley on LinkedIn](https://www.linkedin.com/in/dave-farley-a67927/)
- [The parity test, ParityTests.cs](https://github.com/SteveStout/TheYard/blob/main/functions/jobhunt.Tests/ParityTests.cs)
- [The decision record, ADR 111: The tracker merger runs in Azure](https://theyard.stevenstout.biz/?doc=adr-the-tracker-merger-runs-in-azure)
- [The LinkedIn post](https://www.linkedin.com/feed/update/urn:li:activity:7519090977731633152/)
- [TheYard on GitHub](https://github.com/SteveStout/TheYard)
