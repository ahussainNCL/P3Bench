import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import LandingPage from "./LandingPage";
import LocalSetup from "./LocalSetup";
import { hosted } from "./runtime";
import "./index.css";
import "./studio.css";

if (window.location.pathname === "/" && window.location.hash === "#bytes") {
  window.location.replace("/app#bytes");
}

const studio = /^\/app\/?$/.test(window.location.pathname);
document.documentElement.style.colorScheme = "light";
document.title = studio ? "P3Bench · Configuration studio" : "P3Bench · Your P3 configuration, clearly in view";

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    {studio ? hosted ? <LocalSetup /> : <div className="studio-shell h-full"><App /></div> : <LandingPage />}
  </React.StrictMode>
);
