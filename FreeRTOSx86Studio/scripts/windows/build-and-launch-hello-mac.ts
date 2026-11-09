import {
  type CandidateRunConfig,
  type RunEvidence,
  sanitizeWindowsPath,
  substituteArtifactArgument,
  validateCandidateRunConfig,
} from "../lib/feasibility.ts";

interface ConfigFile {
  profiles: Record<string, CandidateRunConfig>;
}

function parseArgs(args: string[]): {
  profile: string;
  configPath: string;
  evidencePath: string;
} {
  const values = new Map<string, string>();
  for (let index = 0; index < args.length; index += 1) {
    const key = args[index];
    if (key === "--help" || key === "-h") {
      console.log(
        "Usage: build-and-launch-hello-mac.ts --profile <id> --config <json> [--evidence <json>]",
      );
      Deno.exit(0);
    }
    if (!key?.startsWith("--") || !args[index + 1]) {
      throw new Error(`Expected a value after ${key ?? "argument"}.`);
    }
    values.set(key, args[index + 1]);
    index += 1;
  }
  const profile = values.get("--profile");
  const configPath = values.get("--config");
  if (!profile || !configPath) {
    throw new Error("--profile and --config are required.");
  }
  return {
    profile,
    configPath,
    evidencePath: values.get("--evidence") ??
      "feasibility/evidence/latest-build-launch.json",
  };
}

async function writeEvidence(path: string, report: RunEvidence): Promise<void> {
  const parent = path.replace(/[\\/][^\\/]+$/, "");
  if (parent) await Deno.mkdir(parent, { recursive: true });
  await Deno.writeTextFile(path, `${JSON.stringify(report, null, 2)}\n`);
}

export async function executeCandidateRun(
  profile: string,
  config: CandidateRunConfig,
  evidencePath: string,
  options: { waitForEmulatorExit?: boolean } = {},
): Promise<RunEvidence> {
  validateCandidateRunConfig(profile, config);
  const report: RunEvidence = {
    capturedAt: new Date().toISOString(),
    profile,
    architecture: config.architecture,
    build: {
      command: sanitizeWindowsPath(config.build.command),
      exitCode: -1,
      artifactFound: false,
    },
    emulator: {
      command: sanitizeWindowsPath(config.emulator.command),
      processStarted: false,
    },
    guestBootVerified: false,
    compatibilityState: "not-run",
  };

  const build = new Deno.Command(config.build.command, {
    args: config.build.args,
    ...(config.build.cwd ? { cwd: config.build.cwd } : {}),
    stdin: "inherit",
    stdout: "inherit",
    stderr: "inherit",
  }).spawn();
  const buildStatus = await build.status;
  report.build.exitCode = buildStatus.code;
  if (buildStatus.code !== 0) {
    await writeEvidence(evidencePath, report);
    throw new Error(`Build command failed with exit code ${buildStatus.code}.`);
  }

  try {
    await Deno.stat(config.artifactPath);
    report.build.artifactFound = true;
  } catch {
    await writeEvidence(evidencePath, report);
    throw new Error(
      `Build succeeded but artifact was not found: ${config.artifactPath}`,
    );
  }

  const emulatorArgs = substituteArtifactArgument(
    config.emulator.args,
    config.artifactPath,
  );
  const launchTemp = await Deno.makeTempDir({
    prefix: "classic-mac-emulator-",
  });
  const launchSpecPath = `${launchTemp}/launch.json`;
  const stdoutPath = `${launchTemp}/stdout.log`;
  const stderrPath = `${launchTemp}/stderr.log`;
  try {
    await Deno.writeTextFile(
      launchSpecPath,
      `${
        JSON.stringify({
          command: config.emulator.command,
          args: emulatorArgs,
          ...(config.emulator.cwd ? { cwd: config.emulator.cwd } : {}),
          ...(options.waitForEmulatorExit
            ? {
              waitForExit: true,
              stdoutPath,
              stderrPath,
            }
            : {}),
        })
      }\n`,
    );
    const helperPath = await Deno.realPath(
      new URL("./start-emulator.ps1", import.meta.url),
    );
    const launched = await new Deno.Command("powershell.exe", {
      args: [
        "-NoProfile",
        "-NonInteractive",
        "-File",
        helperPath,
        launchSpecPath,
      ],
      stdout: "piped",
      stderr: "piped",
    }).output();
    const launchOutput = new TextDecoder().decode(launched.stdout).trim();
    const launchMatch = /^(\d+)(?:\|(\d+))?$/.exec(launchOutput);
    if (launched.code !== 0 || !launchMatch) {
      const errorOutput = [
        new TextDecoder().decode(launched.stderr).trim(),
        await Deno.readTextFile(stderrPath).catch(() => ""),
      ].filter(Boolean).join(" ");
      await writeEvidence(evidencePath, report);
      throw new Error(
        `Emulator process could not be started: ${
          errorOutput || launchOutput || `PowerShell exited ${launched.code}`
        }`,
      );
    }
    report.emulator.processStarted = true;
    report.emulator.processId = Number(launchMatch[1]);
    if (launchMatch[2] && Number(launchMatch[2]) !== 0) {
      const stderr = await Deno.readTextFile(stderrPath).catch(() => "");
      await writeEvidence(evidencePath, report);
      throw new Error(
        `Emulator process exited with code ${
          launchMatch[2]
        } during the optional wait.${stderr.trim() ? ` ${stderr.trim()}` : ""}`,
      );
    }
  } finally {
    await Deno.remove(launchTemp, { recursive: true }).catch(() => undefined);
  }
  await writeEvidence(evidencePath, report);
  return report;
}

async function main(): Promise<void> {
  const { profile, configPath, evidencePath } = parseArgs(Deno.args);
  const file = JSON.parse(await Deno.readTextFile(configPath)) as ConfigFile;
  const config = file.profiles?.[profile];
  if (!config) {
    throw new Error(`Profile "${profile}" is not present in the config.`);
  }
  const report = await executeCandidateRun(profile, config, evidencePath);
  console.log(JSON.stringify(report, null, 2));
  console.log(
    "Emulator process started only; guest boot/application launch remains unverified.",
  );
  console.log(`Evidence written to ${evidencePath}`);
}

if (import.meta.main) {
  try {
    await main();
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    Deno.exit(1);
  }
}
