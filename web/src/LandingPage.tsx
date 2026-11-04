import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent } from "react";
import "./landing.css";
import "./landing-polish.css";

function Mark() {
  return <svg viewBox="0 0 40 40" fill="none" aria-hidden="true"><path d="m20 3 15 8.5v17L20 37 5 28.5v-17L20 3Z" stroke="currentColor" strokeWidth="2"/><path d="m20 12 7 4v8l-7 4-7-4v-8l7-4Z" fill="currentColor"/></svg>;
}

function Arrow() {
  return <svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M5 12h14m-6-6 6 6-6 6" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"/></svg>;
}

const tabs = ["Configuration", "Byte preview", "Write plan"] as const;

function StudioPreview({ active, setActive }: { active: number; setActive: (index: number) => void }) {
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
        <div className="preview-metrics"><div><span>DEMO BLOCK</span><strong>254<small> bytes</small></strong><i/></div><div><span>DEMO CATALOGUE</span><strong>34<small> parameters</small></strong><i/></div><div><span>WORKSPACE</span><strong>Local<small> by design</small></strong><i/></div></div>
        <div className="preview-tabs" role="tablist" aria-label="Studio preview">{tabs.map((tab, index) => <button key={tab} ref={el => { buttons.current[index] = el; }} id={`preview-tab-${index}`} role="tab" aria-selected={active === index} aria-controls={`preview-panel-${index}`} tabIndex={active === index ? 0 : -1} onKeyDown={event => navigate(event, index)} onClick={() => setActive(index)}>{tab}</button>)}</div>
        <div key={active} className="preview-panel" id={`preview-panel-${active}`} role="tabpanel" aria-labelledby={`preview-tab-${active}`} tabIndex={0}>
          {active === 0 ? <><div className="preview-table-head"><span>PARAMETER</span><span>DEMO VALUE</span></div>{[["Daytime running lights", "Enabled", "toggle"], ["Approach lighting", "Enabled", "toggle"], ["Follow-me-home duration", "30 seconds", "value"], ["Market variant", "Europe", "value"]].map(([label, value, kind]) => <div className="preview-row" key={label}><span>{label}</span><span className="preview-setting">{value}{kind === "toggle" ? <i className="demo-toggle"/> : <span className="preview-chevron">⌄</span>}</span></div>)}</> : active === 1 ? <><div className="preview-table-head"><span>OFFSET / HEXADECIMAL</span><span>254 BYTES</span></div><div className="demo-hex">{["0000", "0008", "0010", "0018"].map((offset, row) => <div key={offset}><span>{offset}</span>{Array.from({length: 8}, (_, col) => <b className={row === 1 && col === 4 ? "changed-byte" : ""} key={col}>{row === 1 && col === 4 ? "04" : ["00", "1E", "02", "FF"][(row + col) % 4]}</b>)}</div>)}</div><div className="preview-note">Orange highlights show the candidate byte changes.</div></> : <><div className="preview-table-head"><span>STAGED CHANGE</span><span>BEFORE → AFTER</span></div><div className="preview-row"><span>Approach lighting</span><span className="plan-value">Disabled → Enabled</span></div><div className="preview-row"><span>Byte 0x000C</span><span className="plan-value">00 → 04</span></div><div className="preview-row"><span>Checksum</span><span className="checksum">Recomputed</span></div><div className="preview-note">Illustration only. No changes are applied from this preview.</div></>}
        </div><div className="preview-bottom"><span><span className="green-dot"/> Synthetic catalogue</span><span>Read → Review → Apply in simulator</span></div>
      </div>
    </div>
  </div>;
}

export default function LandingPage() {
  const [menu, setMenu] = useState(false);
  const [previewTab, setPreviewTab] = useState(0);
  const root = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const page = root.current;
    if (!page || !('IntersectionObserver' in window) || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const items = page.querySelectorAll('[data-reveal]');
    const observer = new IntersectionObserver(entries => {
      entries.forEach(entry => {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-visible');
          observer.unobserve(entry.target);
        }
      });
    }, { threshold: 0.08 });
    page.classList.add('reveal-enabled');
    items.forEach(item => observer.observe(item));
    return () => { observer.disconnect(); page.classList.remove('reveal-enabled'); };
  }, []);
  return <div ref={root} className="orbit-landing orbit-refined">
    <a className="orbit-skip" href="#main">Skip to content</a>
    <header className="orbit-header"><a className="orbit-brand" href="/" aria-label="P3Bench home"><Mark/><span>P3Bench<small>CONFIGURATION STUDIO</small></span></a><button className="orbit-menu-button" aria-label={menu ? "Close navigation" : "Open navigation"} aria-expanded={menu} aria-controls="landing-navigation" onClick={() => setMenu(!menu)}>{menu ? "Close −" : "Menu +"}</button><nav id="landing-navigation" className={menu ? "is-open" : ""} aria-label="Main navigation" onKeyDown={event => { if (event.key === "Escape") { setMenu(false); document.querySelector<HTMLButtonElement>(".orbit-menu-button")?.focus(); } }}><a href="#features" onClick={() => setMenu(false)}>Features</a><a href="#workflow" onClick={() => setMenu(false)}>How it works</a><a href="#support" onClick={() => setMenu(false)}>Vehicle support</a><a className="orbit-button small" href="/app">Open studio <Arrow/></a></nav></header>
    <main id="main">
      <section className="orbit-hero"><div className="orbit-eyebrow"><span/> BUILT FOR THE VOLVO P3 PLATFORM</div><h1>Your P3 configuration,<br/><span>clearly in view.</span></h1><p>Understand your settings. Review every byte.<br className="desktop-break"/> A local configuration studio that puts you in control.</p><div className="orbit-actions"><a className="orbit-button" href="/app">Open studio <Arrow/></a><a className="orbit-button secondary" href="#features">Explore features <span aria-hidden="true">↓</span></a></div><div className="hero-assurance"><span>No account required</span><i/><span>Local by design</span><i/><span>Simulator included</span></div></section>
      <section id="studio-preview" className="orbit-showcase" aria-label="Studio preview"><StudioPreview active={previewTab} setActive={setPreviewTab}/><div className="showcase-caption"><span>YOUR WORKSPACE. YOUR CONFIGURATION.</span><span>Interactive preview · synthetic data</span></div></section>
      <section id="features" className="orbit-features orbit-section">
        <div className="section-intro" data-reveal><div><span className="orbit-eyebrow">A CLOSER LOOK AT YOUR CONFIGURATION</span><h2>Clarity at every layer.</h2></div><p>From a familiar setting to the byte behind it. One workspace, with the details always in reach.</p></div>
        <div className="feature-grid">
          <article data-reveal><div className="feature-copy"><span className="feature-number">01 / EXPLORE</span><h3>Your settings.<br/>Without the guesswork.</h3><p>Browse grouped parameters, search the catalogue, and stage edits in one place. Your changes stay pending until you review them.</p><a className="feature-link" href="/app">Explore the studio <Arrow/></a></div><div className="feature-art settings-art" aria-hidden="true"><div className="art-window-label"><i/> CONFIGURATION / DEMO</div><span>Lighting & visibility <b>⌄</b></span><span>Approach lighting <i/></span><span>Follow-me-home <em>30 s</em></span><div className="art-footer"><span/> Staged locally. Ready for review.</div></div></article>
          <article data-reveal><div className="feature-copy"><span className="feature-number">02 / UNDERSTAND</span><h3>A small change.<br/>The full picture.</h3><p>Compare old and new hex values, inspect raw bytes, and review the recomputed checksum before applying a simulator plan.</p><a className="feature-link" href="#studio-preview" onClick={() => setPreviewTab(1)}>Try the byte preview <Arrow/></a></div><div className="feature-art bytes-art" aria-hidden="true"><div className="art-window-label"><i/> WRITE PLAN / ILLUSTRATION</div><span>OFFSET <b>BEFORE</b><b>AFTER</b></span><span>0x000C <b>12</b><b className="byte-accent">16</b></span><span>0x0001 <b>0B</b><b className="byte-accent">0F</b></span><div className="art-footer"><span/> Every candidate byte, visible.</div></div></article>
          <article data-reveal><div className="feature-copy"><span className="feature-number">03 / KEEP</span><h3>Your work stays<br/>in your hands.</h3><p>Import a hex dump or your own catalogue. Export configuration and provenance as JSON to keep a record of your work.</p><a className="feature-link" href="/app">Open your workspace <Arrow/></a></div><div className="feature-art files-art" aria-hidden="true"><div className="export-document"><span className="export-icon">↗</span><b>p3-block.json</b><small>CONFIGURATION EXPORT</small><div className="export-lines"><i/><i/><i/></div><span className="file-badge">JSON · LOCAL EXPORT</span></div></div></article>
        </div>
      </section>
      <section id="workflow" className="orbit-workflow"><div className="orbit-section"><span className="orbit-eyebrow">A DELIBERATE WORKFLOW</span><div data-reveal className="workflow-heading"><h2>Read. Review.<br/><span>Then take the next step.</span></h2><p>Start with the included simulator.<br/>Explore the complete workflow without a car.</p></div><div className="workflow-steps">{[["01", "Read", "Load a synthetic configuration block, or import a hex dump to inspect with the active catalogue."], ["02", "Review", "Stage your settings and preview the byte-level plan. See exactly what would change."], ["03", "Apply in simulator", "Confirm the plan to update the simulated block, then read it back to check the result."]].map(([number, title, text]) => <article data-reveal key={number}><span>{number}</span><h3>{title}</h3><p>{text}</p></article>)}</div><div className="workflow-note"><span className="note-symbol">i</span><p>The supplied catalogue is synthetic demo data. Hardware access requires the local bridge and a compatible J2534 adapter. Vehicle configuration writing is unavailable.</p></div></div></section>
      <section id="support" data-reveal className="orbit-platform orbit-section"><span className="orbit-eyebrow">VEHICLE SUPPORT</span><h2>Start on the bench.</h2><p>Explore the studio with the included synthetic P3 simulator.</p><small>Real vehicle configuration support depends on a verified profile. No verified vehicle configuration-read profile is included, and vehicle configuration writing is unavailable.</small></section>
      <section data-reveal className="orbit-final orbit-section"><div className="final-mark"><Mark/></div><span className="orbit-eyebrow">GET TO KNOW YOUR CONFIGURATION</span><h2>A little more insight.<br/>A lot more control.</h2><p>Your studio is ready. Start exploring with the simulator.</p><a className="orbit-button" href="/app">Open studio <Arrow/></a><small>Runs locally. No account. No telemetry.</small></section>
    </main><footer className="orbit-footer"><div><a className="orbit-brand" href="/"><Mark/><span>P3Bench<small>CONFIGURATION STUDIO</small></span></a><span>Built for curiosity. Designed for clarity.</span></div><div className="footer-bottom"><p>Independent project. Not affiliated with Volvo, OrBit, VIDA, or any dealer tool.<br/>Vehicle and product names belong to their respective owners.</p><a href="#main">Back to top ↑</a></div></footer>
  </div>;
}
