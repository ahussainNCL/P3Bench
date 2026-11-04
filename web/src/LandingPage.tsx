import { useRef, useState } from "react";
import type { KeyboardEvent } from "react";
import "./landing.css";

function Mark() {
  return <svg viewBox="0 0 40 40" fill="none" aria-hidden="true"><path d="m20 3 15 8.5v17L20 37 5 28.5v-17L20 3Z" stroke="currentColor" strokeWidth="2"/><path d="m20 12 7 4v8l-7 4-7-4v-8l7-4Z" fill="currentColor"/></svg>;
}

function Arrow() {
  return <svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M5 12h14m-6-6 6 6-6 6" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"/></svg>;
}

const tabs = ["Configuration", "Byte preview", "Write plan"] as const;

function StudioPreview() {
  const [active, setActive] = useState(0);
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  function navigate(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    let next = index;
    if (event.key === "ArrowRight") next = (index + 1) % tabs.length;
    else if (event.key === "ArrowLeft") next = (index + tabs.length - 1) % tabs.length;
    else if (event.key === "Home") next = 0;
    else if (event.key === "End") next = tabs.length - 1;
    else return;
    event.preventDefault();
    setActive(next);
    buttons.current[next]?.focus();
  }
  return <div className="orbit-preview" aria-label="Interactive studio illustration">
    <div className="preview-title"><span className="window-dots"><i/><i/><i/></span><span>P3BENCH <b>/</b> CONFIGURATION STUDIO</span><span className="preview-status"><i/> SIMULATOR</span></div>
    <div className="preview-body">
      <aside className="preview-sidebar"><div className="preview-car"><Mark/><span>VOLVO P3<small>Synthetic demo profile</small></span></div><span className="preview-caption">WORKSPACE</span><div className="preview-nav selected">Configuration <span>34</span></div><div className="preview-nav">Raw bytes <span>254 B</span></div><div className="preview-nav">Session log</div><div className="preview-local"><span className="green-dot"/> Local session<small>Your data stays with you.</small></div></aside>
      <div className="preview-content"><div className="preview-heading"><div><span className="preview-caption">CENTRAL ELECTRONIC MODULE</span><h3>A clearer view of every setting.</h3></div><span className="demo-pill">DEMO DATA</span></div>
        <div className="preview-tabs" role="tablist" aria-label="Studio preview">{tabs.map((tab, index) => <button key={tab} ref={el => { buttons.current[index] = el; }} id={`preview-tab-${index}`} role="tab" aria-selected={active === index} aria-controls={`preview-panel-${index}`} tabIndex={active === index ? 0 : -1} onKeyDown={event => navigate(event, index)} onClick={() => setActive(index)}>{tab}</button>)}</div>
        <div className="preview-panel" id={`preview-panel-${active}`} role="tabpanel" aria-labelledby={`preview-tab-${active}`} tabIndex={0}>
          {active === 0 ? <><div className="preview-table-head"><span>PARAMETER</span><span>DEMO VALUE</span></div>{[["Daytime running lights", "Enabled", "toggle"], ["Welcome lighting", "Enabled", "toggle"], ["Follow-me-home duration", "30 seconds", "value"], ["Market variant", "Europe", "value"]].map(([label, value, kind]) => <div className="preview-row" key={label}><span>{label}</span><span className="preview-setting">{value}{kind === "toggle" ? <i className="demo-toggle"/> : <span className="preview-chevron">⌄</span>}</span></div>)}</> : active === 1 ? <><div className="preview-table-head"><span>OFFSET / HEXADECIMAL</span><span>254 BYTES</span></div><div className="demo-hex">{["0000", "0010", "0020", "0030"].map((offset, row) => <div key={offset}><span>{offset}</span>{Array.from({length: 8}, (_, col) => <b className={row === 1 && col === 3 ? "changed-byte" : ""} key={col}>{row === 1 && col === 3 ? "01" : ["00", "1E", "02", "FF"][(row + col) % 4]}</b>)}</div>)}</div><div className="preview-note">Orange highlights show the candidate byte changes.</div></> : <><div className="preview-table-head"><span>STAGED CHANGE</span><span>BEFORE → AFTER</span></div><div className="preview-row"><span>Welcome lighting</span><span className="plan-value">Disabled → Enabled</span></div><div className="preview-row"><span>Byte 0x0013</span><span className="plan-value">00 → 01</span></div><div className="preview-row"><span>Checksum</span><span className="checksum">Recomputed</span></div><div className="preview-note">Illustration only. No changes are applied from this preview.</div></>}
        </div><div className="preview-bottom"><span><span className="green-dot"/> Synthetic catalogue</span><span>Read → Review → Apply in simulator</span></div>
      </div>
    </div>
  </div>;
}

export default function LandingPage() {
  const [menu, setMenu] = useState(false);
  return <div className="orbit-landing">
    <a className="orbit-skip" href="#main">Skip to content</a>
    <header className="orbit-header"><a className="orbit-brand" href="/" aria-label="P3Bench home"><Mark/><span>P3Bench<small>CONFIGURATION STUDIO</small></span></a><button className="orbit-menu-button" aria-label={menu ? "Close navigation" : "Open navigation"} aria-expanded={menu} aria-controls="landing-navigation" onClick={() => setMenu(!menu)}>{menu ? "Close −" : "Menu +"}</button><nav id="landing-navigation" className={menu ? "is-open" : ""} aria-label="Main navigation" onKeyDown={event => { if (event.key === "Escape") { setMenu(false); document.querySelector<HTMLButtonElement>(".orbit-menu-button")?.focus(); } }}><a href="#features" onClick={() => setMenu(false)}>Features</a><a href="#workflow" onClick={() => setMenu(false)}>How it works</a><a className="orbit-button small" href="/app">Open studio <Arrow/></a></nav></header>
    <main id="main">
      <section className="orbit-hero"><div className="orbit-eyebrow"><span/> BUILT FOR THE VOLVO P3 PLATFORM</div><h1>Your P3 configuration,<br/><span>clearly in view.</span></h1><p>Understand your settings. Review every byte.<br className="desktop-break"/> A local configuration studio that puts you in control.</p><div className="orbit-actions"><a className="orbit-button" href="/app">Open studio <Arrow/></a><a className="orbit-button secondary" href="#features">Explore features <span aria-hidden="true">↓</span></a></div><div className="hero-assurance"><span>No account required</span><i/><span>Local by design</span><i/><span>Simulator included</span></div></section>
      <section className="orbit-showcase" aria-label="Studio preview"><StudioPreview/><div className="showcase-caption"><span>YOUR WORKSPACE. YOUR CONFIGURATION.</span><span>Interactive preview · synthetic data</span></div></section>
      <section id="features" className="orbit-features orbit-section"><div className="section-intro"><div><span className="orbit-eyebrow">LESS GUESSWORK. MORE CLARITY.</span><h2>From settings<br/>to the bytes behind them.</h2></div><p>A focused workspace for exploring P3 configuration. See what you have, stage what you want, and understand the difference.</p></div><div className="feature-grid"><article><span className="feature-number">01 / CONFIGURE</span><div className="feature-art settings-art" aria-hidden="true"><span>Lighting & visibility <b>⌄</b></span><span>Welcome lighting <i/></span><span>Follow-me-home <em>30 s</em></span></div><h3>Settings that make sense.</h3><p>Browse grouped parameters, search the catalogue, and stage edits in one place. Your changes stay pending until you review them.</p></article><article><span className="feature-number">02 / INSPECT</span><div className="feature-art bytes-art" aria-hidden="true"><span>OFFSET <b>BEFORE</b><b>AFTER</b></span><span>0x0013 <b>00</b><b className="byte-accent">01</b></span><span>0x00FD <b>4A</b><b className="byte-accent">4B</b></span></div><h3>Every change, visible.</h3><p>Compare old and new hex values, inspect raw bytes, and review the recomputed checksum before applying a simulator plan.</p></article><article><span className="feature-number">03 / KEEP</span><div className="feature-art files-art" aria-hidden="true"><div><span>↗</span><b>p3-block.json</b><small>CONFIGURATION EXPORT</small></div><span className="file-badge">JSON</span></div><h3>Your data, in your hands.</h3><p>Import a hex dump or your own catalogue. Export configuration and provenance as JSON to keep a record of your work.</p></article></div></section>
      <section id="workflow" className="orbit-workflow"><div className="orbit-section"><span className="orbit-eyebrow">A DELIBERATE WORKFLOW</span><div className="workflow-heading"><h2>Read. Review.<br/><span>Then take the next step.</span></h2><p>Start with the included simulator.<br/>Explore the complete workflow without a car.</p></div><div className="workflow-steps">{[["01", "Read", "Load a synthetic configuration block, or import a hex dump to inspect with the active catalogue."], ["02", "Review", "Stage your settings and preview the byte-level plan. See exactly what would change."], ["03", "Apply in simulator", "Confirm the plan to update the simulated block, then read it back to check the result."]].map(([number, title, text]) => <article key={number}><span>{number}</span><h3>{title}</h3><p>{text}</p></article>)}</div><div className="workflow-note"><span className="note-symbol">i</span><p>The supplied catalogue is synthetic demo data. Hardware access requires the local bridge and a compatible J2534 adapter. Vehicle configuration writing is unavailable.</p></div></div></section>
      <section className="orbit-platform orbit-section"><span className="orbit-eyebrow">VEHICLE SUPPORT</span><h2>Start on the bench.</h2><p>Explore the studio with the included synthetic P3 simulator.</p><small>Real vehicle configuration support depends on a verified profile. No verified vehicle configuration-read profile is included, and vehicle configuration writing is unavailable.</small></section>
      <section className="orbit-final orbit-section"><div className="final-mark"><Mark/></div><span className="orbit-eyebrow">GET TO KNOW YOUR CONFIGURATION</span><h2>A little more insight.<br/>A lot more control.</h2><p>Your studio is ready. Start exploring with the simulator.</p><a className="orbit-button" href="/app">Open studio <Arrow/></a><small>Runs locally. No account. No telemetry.</small></section>
    </main><footer className="orbit-footer"><div><a className="orbit-brand" href="/"><Mark/><span>P3Bench<small>CONFIGURATION STUDIO</small></span></a><span>Built for curiosity. Designed for clarity.</span></div><div className="footer-bottom"><p>Independent project. Not affiliated with Volvo, OrBit, VIDA, or any dealer tool.<br/>Vehicle and product names belong to their respective owners.</p><a href="#main">Back to top ↑</a></div></footer>
  </div>;
}
