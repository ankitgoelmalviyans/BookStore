import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NavToolbarComponent } from './shared/nav-toolbar/nav-toolbar.component';
import { HelpAssistantComponent } from './help-assistant/help-assistant.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, NavToolbarComponent, HelpAssistantComponent],
  template: `
    <app-nav-toolbar></app-nav-toolbar>
    <router-outlet></router-outlet>
    <app-help-assistant></app-help-assistant>
  `
})
export class AppComponent {}
