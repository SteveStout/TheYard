# CI/CD

How code gets from a laptop to https://theyard.stevenstout.biz, written for a
hiring manager or anyone learning how small projects ship safely.

## What runs today

**Continuous integration.** Every push to GitHub runs the full test wall: the
API suite, the frontend suite, and a browser suite that clicks through the
running app the way a person would. A change that fails any of them does not
ship.

![Twenty green CI runs on GitHub Actions](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/github-ci-green.jpg)

**The edge deploys itself.** The HTTPS front door of this site (see the
Hosting menu) is three files in this repository. Netlify watches the repo and
redeploys the edge on every push, with no human in the loop.

**The container ships by scripted pipeline.** The application image is built,
pushed to Azure Container Registry, and rolled onto Azure Container Instances
by one script: the same steps in the same order every time, logged end to end,
and gated on the test wall passing first.

## What comes next

The scripted leg becomes a GitHub Actions workflow: on merge to main, build
the image, push it to the registry, and roll the container, authenticated with
OIDC so no credential lives in the pipeline. Documentation lands here as that
work happens, 2026-09-02 and 2026-09-03.

## Why it is built in this order

Tests first, automation second. A pipeline that ships untested code is a
faster way to break production. The test wall existed before this site had a
URL, and every deploy so far has been gated on it.
