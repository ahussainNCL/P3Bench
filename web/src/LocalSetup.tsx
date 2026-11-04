import { studioUrl } from "./runtime";
import "./landing.css";

export default function LocalSetup() {
  return <div className="orbit-landing">
    <main className="orbit-section local-setup">
      <a className="orbit-brand" href="/">P3Bench</a>
      <span className="orbit-eyebrow">LOCAL CONFIGURATION STUDIO</span>
      <h1>Start the studio on your computer.</h1>
      <p>This website introduces P3Bench. The simulator and configuration tools run through the local Windows app.</p>
      <ol>
        <li><a href="https://github.com/ahussainNCL/P3Bench/archive/refs/heads/main.zip">Download P3Bench</a> and extract the ZIP.</li>
        <li>Make sure Node.js 22 or newer and .NET Framework 4.8 are installed.</li>
        <li>Open the extracted folder and double-click <code>start.cmd</code>. Keep its console window open.</li>
        <li>Choose <strong>Open local studio</strong> below. The simulator does not require a vehicle or adapter.</li>
      </ol>
      <a className="orbit-button" href={studioUrl}>Open local studio <span aria-hidden="true">↗</span></a>
      <p className="setup-note">Already running? Use the same computer that is running the bridge. If the local page cannot be reached, check that the start.cmd console is still open and shows no startup errors.</p>
      <a href="/">← Back to homepage</a>
    </main>
  </div>;
}
