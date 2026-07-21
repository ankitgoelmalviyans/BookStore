import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NavToolbarComponent } from './shared/nav-toolbar/nav-toolbar.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, NavToolbarComponent],
  template: `
    <app-nav-toolbar></app-nav-toolbar>
    <router-outlet></router-outlet>
  `
})
export class AppComponent {}
