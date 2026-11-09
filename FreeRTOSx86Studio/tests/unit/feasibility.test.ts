import {
  type CandidateRunConfig,
  evaluateProfileExitGate,
  evaluateToolchainReadiness,
  parseWslDistributionList,
  parseWslToolChecks,
  sanitizeWindowsPath,
  substituteArtifactArgument,
  validateCandidateRunConfig,
} from "../../scripts/lib/feasibility.ts";
import { executeCandidateRun } from "../../scripts/windows/build-and-launch-hello-mac.ts";

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

function same(actual: unknown, expected: unknown, label: string): void {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(
      `${label}\nExpected: ${JSON.stringify(expected)}\nActual:   ${
        JSON.stringify(actual)
      }`,
    );
  }
}

Deno.test("WSL distro parsing handles Windows NUL bytes and the default marker", () => {
  same(
    parseWslDistributionList(
      "\0U\0b\0u\0n\0t\0u\0\r\n*\0k\0a\0l\0i\0-\0l\0i\0n\0u\0x\0\r\n",
    ),
    ["Ubuntu", "kali-linux"],
    "distribution names",
  );
});

Deno.test("WSL tool parsing preserves found and missing candidate tools", () => {
  const tools = parseWslToolChecks(
    "m68k-apple-macos-gcc=/opt/retro68/bin/m68k-apple-macos-gcc\n" +
      "powerpc-apple-macos-gcc=NOT_FOUND\n",
  );
  assert(
    tools.find((tool) => tool.id === "m68k-apple-macos-gcc")?.state ===
      "available",
    "68k compiler should be found",
  );
  assert(
    tools.find((tool) => tool.id === "powerpc-apple-macos-gcc")?.state ===
      "missing",
    "PowerPC compiler should be reported missing",
  );
});

Deno.test("tool evidence redacts the current Windows user folder", () => {
  same(
    sanitizeWindowsPath("C:\\Users\\sample-user\\.deno\\bin\\deno.exe"),
    "%USERPROFILE%\\.deno\\bin\\deno.exe",
    "sanitized executable path",
  );
  same(
    sanitizeWindowsPath("C:\\Program Files\\CMake\\bin\\cmake.exe"),
    "C:\\Program Files\\CMake\\bin\\cmake.exe",
    "system paths remain actionable",
  );
});

Deno.test("tool presence alone never permits compatibility claims", () => {
  const result = evaluateToolchainReadiness(
    [
      { id: "deno", state: "available" },
      { id: "rustc", state: "available" },
      { id: "cargo", state: "available" },
      { id: "tauri", state: "available" },
      { id: "wsl", state: "available" },
      { id: "BasiliskII.exe", state: "available" },
      { id: "SheepShaver.exe", state: "available" },
    ],
    [{
      name: "Ubuntu",
      state: "available",
      tools: [
        { id: "rustup", state: "available" },
        { id: "m68k-apple-macos-gcc", state: "available" },
        { id: "powerpc-apple-macos-gcc", state: "available" },
        { id: "Rez", state: "available" },
        { id: "BasiliskII", state: "available" },
        { id: "SheepShaver", state: "available" },
      ],
    }],
  );
  assert(
    result.desktopHostReady,
    "Windows host prerequisites should be detected",
  );
  assert(
    result.legacyBuildAndLaunchReady,
    "tool presence should be reported separately",
  );
  assert(
    result.compatibilityClaimsAllowed === false,
    "tool presence cannot validate a guest profile",
  );

  const missingRust = evaluateToolchainReadiness(
    [
      { id: "deno", state: "available" },
      { id: "wsl", state: "available" },
    ],
    [],
  );
  assert(
    !missingRust.desktopHostReady,
    "a missing desktop Rust/Tauri toolchain must block host readiness",
  );
});

Deno.test("emulator receives exactly one explicit build artifact argument", () => {
  same(
    substituteArtifactArgument(
      ["--prefs", "config", "{artifact}"],
      "C:/work/app.dsk",
    ),
    ["--prefs", "config", "C:/work/app.dsk"],
    "substituted emulator arguments",
  );
  let rejected = false;
  try {
    substituteArtifactArgument(["--prefs", "config"], "C:/work/app.dsk");
  } catch {
    rejected = true;
  }
  assert(rejected, "missing artifact placeholder must be rejected");
});

Deno.test("spike exit gate stays closed until each profile has build and guest-launch evidence", () => {
  const pending = evaluateProfileExitGate([{
    id: "mac-se-68k",
    architecture: "68k",
    state: "candidate",
    buildVerified: true,
    guestLaunchVerified: false,
  }]);
  assert(!pending.ready, "a candidate without guest evidence must not pass");
  same(pending.pendingProfiles, ["mac-se-68k"], "pending profile ids");

  const complete = evaluateProfileExitGate([{
    id: "mac-se-68k",
    architecture: "68k",
    state: "verified",
    buildVerified: true,
    guestLaunchVerified: true,
  }]);
  assert(
    complete.ready,
    "a fully verified profile should pass the profile gate",
  );
});

Deno.test("runner builds before launch and records process-start separately from guest boot", async () => {
  const temp = await Deno.makeTempDir({ prefix: "classic-mac-spike-" });
  try {
    const artifact = `${temp}/hello.dsk`;
    const marker = `${temp}/launch-marker.txt`;
    const evidence = `${temp}/evidence.json`;
    const fixture = `${Deno.cwd()}/tests/fixtures/fake-spike-command.ts`;
    const config: CandidateRunConfig = {
      architecture: "68k",
      artifactPath: artifact,
      build: {
        command: Deno.execPath(),
        args: ["run", "--allow-write", fixture, "build", artifact],
      },
      emulator: {
        command: Deno.execPath(),
        args: [
          "run",
          "--allow-read",
          "--allow-write",
          fixture,
          "launch",
          "{artifact}",
          marker,
        ],
      },
    };
    const report = await executeCandidateRun(
      "test-profile",
      config,
      evidence,
      { waitForEmulatorExit: true },
    );
    assert(report.build.exitCode === 0, "fake build should succeed");
    assert(report.build.artifactFound, "fake build artifact should exist");
    assert(report.emulator.processStarted, "emulator process should start");
    assert(
      report.guestBootVerified === false,
      "process start must not claim guest boot",
    );
    assert(
      !report.build.command.includes("\\Users\\"),
      "machine-specific Windows user folders should not be recorded in evidence",
    );
    const written = JSON.parse(await Deno.readTextFile(evidence));
    assert(
      written.compatibilityState === "not-run",
      "evidence must remain unverified",
    );

    let markerCreated = false;
    for (let attempt = 0; attempt < 30; attempt += 1) {
      try {
        await Deno.stat(marker);
        markerCreated = true;
        break;
      } catch {
        await new Promise((resolve) => setTimeout(resolve, 50));
      }
    }
    assert(
      markerCreated,
      "fake emulator command should receive the artifact and start",
    );
  } finally {
    await Deno.remove(temp, { recursive: true });
  }
});

Deno.test("runner refuses to launch after a failed build", async () => {
  const temp = await Deno.makeTempDir({ prefix: "classic-mac-spike-fail-" });
  try {
    const marker = `${temp}/launch-marker.txt`;
    const fixture = `${Deno.cwd()}/tests/fixtures/fake-spike-command.ts`;
    const config: CandidateRunConfig = {
      architecture: "PowerPC",
      artifactPath: `${temp}/missing.dsk`,
      build: {
        command: Deno.execPath(),
        args: ["eval", "Deno.exit(7)"],
      },
      emulator: {
        command: Deno.execPath(),
        args: ["run", "--allow-write", fixture, "launch", "{artifact}", marker],
      },
    };
    let rejected = false;
    try {
      await executeCandidateRun(
        "test-failure",
        config,
        `${temp}/evidence.json`,
      );
    } catch {
      rejected = true;
    }
    assert(rejected, "failed build should abort the run");
    let markerExists = true;
    try {
      await Deno.stat(marker);
    } catch {
      markerExists = false;
    }
    assert(!markerExists, "emulator must not start after a failed build");
  } finally {
    await Deno.remove(temp, { recursive: true });
  }
});

Deno.test("run configuration validates architecture and placeholder before execution", () => {
  const valid: CandidateRunConfig = {
    architecture: "PowerPC",
    artifactPath: "C:/user/hello.dsk",
    build: { command: "wsl.exe", args: ["--distribution", "Ubuntu"] },
    emulator: {
      command: "SheepShaver.exe",
      args: ["--config", "user.cfg", "{artifact}"],
    },
  };
  validateCandidateRunConfig("power-mac-6100", valid);
  let rejected = false;
  try {
    validateCandidateRunConfig("bad", {
      ...valid,
      emulator: { ...valid.emulator, args: ["--config", "user.cfg"] },
    });
  } catch {
    rejected = true;
  }
  assert(rejected, "invalid config must be rejected before starting commands");
});
