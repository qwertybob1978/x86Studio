const [action, outputPath, launchMarker] = Deno.args;
if (!action || !outputPath) throw new Error("Expected action and output path.");

if (action === "build") {
  await Deno.writeTextFile(outputPath, "test artifact");
} else if (action === "launch") {
  if (!launchMarker) throw new Error("Expected launch marker path.");
  await Deno.stat(outputPath);
  await Deno.writeTextFile(launchMarker, "emulator process started");
} else {
  throw new Error(`Unknown fixture action: ${action}`);
}
