import { Component, afterNextRender, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from './auth/auth.service';
import { UserService } from './auth/user.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly user = inject(UserService);
  protected readonly title = signal('Ui');

  // Sign-in state only exists in the browser; rendering it after the first render keeps hydration in step
  // with the prerendered HTML, which has no user.
  protected readonly browserReady = signal(false);

  constructor() {
    afterNextRender(() => this.browserReady.set(true));

    // A permission notice belongs to the page it appeared on.
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationStart),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.user.forbidden.set(false));
  }

  protected signIn(): void {
    void this.auth.login(window.location.pathname);
  }

  protected signOut(): void {
    this.user.reset();
    void this.auth.logout();
  }
}
