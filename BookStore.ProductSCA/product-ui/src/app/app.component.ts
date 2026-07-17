import { Component } from '@angular/core';

@Component({
  selector: 'app-root',
  template: `
    <app-nav-toolbar></app-nav-toolbar>
    <router-outlet></router-outlet>
  `
})
export class AppComponent {}
