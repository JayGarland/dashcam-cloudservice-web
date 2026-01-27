import { initCapturePage } from "./capturePage";

const root = document.getElementById("app");
if (!root) {
  throw new Error("Missing #app root element.");
}

initCapturePage(root);
