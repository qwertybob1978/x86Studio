import {
  evaluateToolchainReadiness,
  parseWslDistributionList,
  parseWslToolChecks,
  sanitizeWindowsPath,
  type ToolchainReport,
  type ToolCheck,
  toolCheck,
  WINDOWS_TOOL_IDS,
  WSL_TOOL_IDS,
} from "../lib/feasibility.ts";

const LOCAL_WINDOWS_TOOL_PATHS: Record<string, string> = {
  "BasiliskII.exe":
    ".local-tools\\emulators\\BasiliskII\\BasiliskII.exe",
  "SheepShaver.exe":
    ".local-tools\\emulators\\SheepShaver\\SheepShaver.exe",
  "mame.exe": ".local-tools\\emulators\\MAME\\mame.exe",
  "Mini vMac.exe":
    ".local-tools\\emulators\\MiniVmac-SE\\Mini vMac.exe",
};

async function run(
  command: string,
  args: string[],
): Promise<{ code: number; stdout: string; stderr: string }> {
  try {
    const result = await new Deno.Command(command, {
      args,
      stdout: "piped",
      stderr: "piped",
    }).output();
    return {
      code: result.code,
      stdout: decodeOutput(result.stdout),
      stderr: decodeOutput(result.stderr),
    };
  } catch (error) {
    return {
      code: 127,
      stdout: "",
      stderr: error instanceof Error ? error.message : String(error),
    };
  }
}

function decodeOutput(bytes: Uint8Array): string {
  const hasUtf16Nulls = bytes.length > 1 &&
    bytes.filter((byte) => byte === 0).length > bytes.length / 5;
  const encoding = hasUtf16Nulls ? "utf-16le" : "utf-8";
  return new TextDecoder(encoding).decode(bytes).replaceAll("\0", "");
}

async function findWindowsTool(id: string): Promise<ToolCheck> {
  const result = await run("where.exe", [id]);
  let path = result.code === 0
    ? result.stdout.split(/\r?\n/).map((line) => line.trim()).find(Boolean)
    : undefined;
  const localPath = LOCAL_WINDOWS_TOOL_PATHS[id];
  if (!path && localPath) {
    try {
      const localFile = await Deno.stat(localPath);
      if (localFile.isFile) path = await Deno.realPath(localPath);
    } catch (error) {
      if (!(error instanceof Deno.errors.NotFound)) throw error;
    }
  }
  return toolCheck(id, path ? sanitizeWindowsPath(path) : undefined);
}

async function readWslVersion(): Promise<string | null> {
  const result = await run("wsl.exe", ["--version"]);
  if (result.code !== 0) return null;
  return result.stdout.match(/\b\d+\.\d+\.\d+(?:\.\d+)?\b/)?.[0] ??
    result.stdout.split(/\r?\n/).map((line) => line.trim()).find(Boolean) ??
    null;
}

async function probeWslDistribution(
  name: string,
): Promise<ToolchainReport["wsl"]["distributions"][number]> {
  const probe = `for tool in ${
    WSL_TOOL_IDS.join(" ")
  }; do path="$(command -v "$tool" 2>/dev/null || true)"; if [ -z "$path" ] && [ -x "$HOME/retro68-build/toolchain/bin/$tool" ]; then path="$HOME/retro68-build/toolchain/bin/$tool"; fi; if [ -n "$path" ]; then printf '%s=%s\\n' "$tool" "\${path/#$HOME/~}"; else printf '%s=NOT_FOUND\\n' "$tool"; fi; done`;
  const result = await run("wsl.exe", [
    "--distribution",
    name,
    "--exec",
    "bash",
    "-c",
    probe,
  ]);
  if (result.code !== 0) {
    return {
      name,
      state: "unknown",
      tools: WSL_TOOL_IDS.map((id) => ({
        id,
        state: "unknown",
        detail: result.stderr.trim() || `WSL probe exited ${result.code}.`,
      })),
      detail: result.stderr.trim() || `WSL probe exited ${result.code}.`,
    };
  }
  const tools = parseWslToolChecks(result.stdout);
  return { name, state: "available", tools };
}

function outputPath(args: string[]): string {
  const index = args.indexOf("--output");
  return index >= 0 && args[index + 1]
    ? args[index + 1]
    : "feasibility/evidence/latest-toolchain-report.json";
}

async function main(): Promise<void> {
  if (Deno.build.os !== "windows") {
    throw new Error(
      "This probe is Windows-triggered; run scripts/windows/verify-toolchain.ps1 from Windows.",
    );
  }

  const windows = await Promise.all(
    WINDOWS_TOOL_IDS.map((id) => findWindowsTool(id)),
  );
  const distroResult = await run("wsl.exe", ["--list", "--quiet"]);
  const distroNames = distroResult.code === 0
    ? parseWslDistributionList(distroResult.stdout)
    : [];
  const distributions = await Promise.all(
    distroNames.map((name) => probeWslDistribution(name)),
  );

  const report: ToolchainReport = {
    capturedAt: new Date().toISOString(),
    host: {
      os: `${Deno.build.os}/${Deno.build.arch}`,
      deno: windows.find((tool) => tool.id === "deno") ??
        toolCheck("deno", undefined),
      windows,
    },
    wsl: {
      version: await readWslVersion(),
      distributions,
    },
    interpretation: evaluateToolchainReadiness(windows, distributions),
  };

  const destination = outputPath(Deno.args);
  const parent = destination.replace(/[\\/][^\\/]+$/, "");
  if (parent) await Deno.mkdir(parent, { recursive: true });
  await Deno.writeTextFile(destination, `${JSON.stringify(report, null, 2)}\n`);
  console.log(JSON.stringify(report, null, 2));
  console.log(`Evidence written to ${destination}`);
}

if (import.meta.main) {
  await main();
}
