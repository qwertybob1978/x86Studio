export type ToolState = "available" | "missing" | "unknown";
export type ProfileState = "candidate" | "not-run" | "verified" | "excluded";
export type Architecture = "68k" | "PowerPC";

export interface ToolCheck {
  id: string;
  state: ToolState;
  path?: string;
  detail?: string;
}

export interface ProfileEvidence {
  id: string;
  architecture: Architecture;
  state: ProfileState;
  buildVerified: boolean;
  guestLaunchVerified: boolean;
  notes?: string;
}

export interface ToolchainReport {
  capturedAt: string;
  host: {
    os: string;
    deno: ToolCheck;
    windows: ToolCheck[];
  };
  wsl: {
    version: string | null;
    distributions: Array<{
      name: string;
      state: ToolState;
      tools: ToolCheck[];
      detail?: string;
    }>;
  };
  interpretation: {
    desktopHostReady: boolean;
    legacyBuildAndLaunchReady: boolean;
    compatibilityClaimsAllowed: false;
  };
}

export interface CommandSpec {
  command: string;
  args: string[];
  cwd?: string;
}

export interface CandidateRunConfig {
  architecture: Architecture;
  artifactPath: string;
  build: CommandSpec;
  emulator: CommandSpec;
}

export interface RunEvidence {
  capturedAt: string;
  profile: string;
  architecture: Architecture;
  build: {
    command: string;
    exitCode: number;
    artifactFound: boolean;
  };
  emulator: {
    command: string;
    processStarted: boolean;
    processId?: number;
  };
  guestBootVerified: false;
  compatibilityState: "not-run";
}

export const WINDOWS_TOOL_IDS = [
  "deno",
  "rustup",
  "rustc",
  "cargo",
  "tauri",
  "git",
  "wsl",
  "cmake",
  "BasiliskII.exe",
  "SheepShaver.exe",
  "mame.exe",
  "Mini vMac.exe",
] as const;

export const WSL_TOOL_IDS = [
  "rustup",
  "cargo",
  "rustc",
  "cmake",
  "make",
  "m68k-apple-macos-gcc",
  "powerpc-apple-macos-gcc",
  "Rez",
  "BasiliskII",
  "SheepShaver",
] as const;

const REQUIRED_WINDOWS_TOOLS = [
  "deno",
  "rustc",
  "cargo",
  "tauri",
  "wsl",
] as const;
const REQUIRED_LEGACY_TOOLS = [
  "m68k-apple-macos-gcc",
  "powerpc-apple-macos-gcc",
  "Rez",
  "BasiliskII",
  "SheepShaver",
] as const;

export function toolCheck(
  id: string,
  path: string | undefined,
  detail?: string,
): ToolCheck {
  return path
    ? { id, state: "available", path, ...(detail ? { detail } : {}) }
    : { id, state: "missing", ...(detail ? { detail } : {}) };
}

export function sanitizeWindowsPath(path: string): string {
  return path.replace(/^[A-Z]:\\Users\\[^\\]+/i, "%USERPROFILE%");
}

export function parseWslDistributionList(output: string): string[] {
  return output
    .replaceAll("\0", "")
    .split(/\r?\n/)
    .map((line) => line.trim().replace(/^\*+\s*/, ""))
    .filter((line) => line.length > 0);
}

export function parseWslToolChecks(output: string): ToolCheck[] {
  const found = new Map<string, string>();
  for (const rawLine of output.replaceAll("\0", "").split(/\r?\n/)) {
    const line = rawLine.trim();
    const separator = line.indexOf("=");
    if (separator <= 0) continue;
    const id = line.slice(0, separator).trim();
    const path = line.slice(separator + 1).trim();
    if (id && path && path !== "NOT_FOUND") found.set(id, path);
    else if (id && path === "NOT_FOUND") found.set(id, "");
  }
  return WSL_TOOL_IDS.map((id) => toolCheck(id, found.get(id) || undefined));
}

export function evaluateToolchainReadiness(
  windows: ToolCheck[],
  wslDistributions: ToolchainReport["wsl"]["distributions"],
): ToolchainReport["interpretation"] {
  const windowsById = new Map(windows.map((tool) => [tool.id, tool]));
  const desktopHostReady = REQUIRED_WINDOWS_TOOLS.every((id) =>
    windowsById.get(id)?.state === "available"
  );
  const legacyFound = wslDistributions.some((distro) =>
    distro.state === "available" &&
    REQUIRED_LEGACY_TOOLS.slice(0, 4).every((id) =>
      distro.tools.find((tool) => tool.id === id)?.state === "available"
    )
  );
  const emulatorPresent = ["BasiliskII.exe", "SheepShaver.exe"].every((
    id,
    index,
  ) =>
    windowsById.get(id)?.state === "available" ||
    wslDistributions.some((distro) =>
      distro.state === "available" &&
      distro.tools.find((tool) => tool.id === REQUIRED_LEGACY_TOOLS[index + 4])
          ?.state === "available"
    )
  );
  return {
    desktopHostReady,
    legacyBuildAndLaunchReady: legacyFound && emulatorPresent,
    compatibilityClaimsAllowed: false,
  };
}

export function substituteArtifactArgument(
  args: string[],
  artifactPath: string,
): string[] {
  const placeholderCount = args.filter((arg) => arg === "{artifact}").length;
  if (placeholderCount !== 1) {
    throw new Error(
      `Emulator args must contain exactly one "{artifact}" argument (found ${placeholderCount}).`,
    );
  }
  return args.map((arg) => arg === "{artifact}" ? artifactPath : arg);
}

export function validateCandidateRunConfig(
  profile: string,
  config: CandidateRunConfig,
): void {
  if (!profile.trim()) throw new Error("Profile id must not be empty.");
  if (config.architecture !== "68k" && config.architecture !== "PowerPC") {
    throw new Error("Architecture must be either 68k or PowerPC.");
  }
  if (!config.artifactPath.trim()) {
    throw new Error("Artifact path must not be empty.");
  }
  for (
    const [label, command] of [
      ["build", config.build],
      ["emulator", config.emulator],
    ] as const
  ) {
    if (!command.command.trim()) {
      throw new Error(`${label} command must not be empty.`);
    }
    if (
      !Array.isArray(command.args) ||
      command.args.some((arg) => typeof arg !== "string")
    ) {
      throw new Error(`${label} args must be an array of strings.`);
    }
  }
  substituteArtifactArgument(config.emulator.args, config.artifactPath);
}

export function evaluateProfileExitGate(
  profiles: ProfileEvidence[],
): { ready: boolean; pendingProfiles: string[] } {
  const pendingProfiles = profiles
    .filter((profile) =>
      profile.state !== "verified" ||
      !profile.buildVerified ||
      !profile.guestLaunchVerified
    )
    .map((profile) => profile.id);
  return {
    ready: profiles.length > 0 && pendingProfiles.length === 0,
    pendingProfiles,
  };
}
